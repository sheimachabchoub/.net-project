using System.Collections.Concurrent;
using MediaLib.Api.Communication;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Agent;

/// <summary>Limite de débit par demandeur (fenêtre glissante d'une minute) et plafond quotidien d'appels au modèle.</summary>
public class LimiteurAgent(TimeProvider horloge, IOptions<AgentOptions> options)
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _fenetres = new();
    private readonly object _verrou = new();
    private DateOnly _jour;
    private int _appelsDuJour;

    public bool AccepterMessage(string demandeur)
    {
        var maintenant = horloge.Maintenant();
        var file = _fenetres.GetOrAdd(demandeur, _ => new Queue<DateTime>());
        lock (file)
        {
            while (file.Count > 0 && maintenant - file.Peek() >= TimeSpan.FromMinutes(1)) file.Dequeue();
            if (file.Count >= options.Value.MessagesParMinute) return false;
            file.Enqueue(maintenant);
            return true;
        }
    }

    public bool ConsommerAppelModele()
    {
        lock (_verrou)
        {
            var aujourdhui = horloge.Aujourdhui();
            if (aujourdhui != _jour)
            {
                _jour = aujourdhui;
                _appelsDuJour = 0;
            }
            if (_appelsDuJour >= options.Value.PlafondQuotidien) return false;
            _appelsDuJour++;
            return true;
        }
    }

    public int AppelsDuJour
    {
        get { lock (_verrou) return _jour == horloge.Aujourdhui() ? _appelsDuJour : 0; }
    }
}

/// <summary>
/// Agent bibliothécaire : choisit le moteur (Claude réel ou simulé), applique limite de débit, plafond quotidien
/// et repli, envoie la réponse sur le canal de la conversation et journalise la décision.
/// </summary>
public class BibliothecaireIA(
    MediaLibDbContext db,
    ContexteDemandeur contexte,
    JournalOutils journalOutils,
    JournalDecisions journal,
    MoteurSimule moteurSimule,
    MoteurClaude moteurClaude,
    LimiteurAgent limiteur,
    ServiceCommunication communication,
    IOptions<AgentOptions> options,
    ILogger<BibliothecaireIA> logger)
{
    public const string MoteurClaudeNom = "claude";
    public const string MoteurSimuleNom = "simule";
    public const string SecoursSansCle = "simule:sans-cle";
    public const string SecoursPanne = "simule:panne";
    public const string SecoursPlafond = "simule:plafond";
    public const string SecoursRefus = "simule:refus";
    public const string Limite = "limite";
    public const string Humain = "humain";

    public bool ClaudeDemande => string.Equals(options.Value.Mode, "claude", StringComparison.OrdinalIgnoreCase);
    public bool ClePresente => !string.IsNullOrWhiteSpace(options.Value.CleApi);

    public async Task<ReponseAgentDto> RepondreAsync(Conversation conversation, string question, Utilisateur? demandeur, IReadOnlyList<string> roles, string declencheur, CancellationToken ct = default)
    {
        contexte.Definir(demandeur, roles, conversation, declencheur);
        journalOutils.Reinitialiser();

        string texte, moteur;
        if (conversation.Etat == EtatConversation.Humain)
        {
            texte = "Un bibliothécaire a repris cette conversation : il vous répondra ici.";
            moteur = Humain;
        }
        else
        {
            (texte, moteur) = await ProduireAsync(conversation, question, ct);
        }

        Message? message = null;
        if (moteur != Humain || conversation.Canal == Canal.Web)
        {
            try
            {
                message = await communication.RepondreAsync(conversation, texte, moteur == Humain ? Message.AuteurSysteme : Message.AuteurAgent, ct);
            }
            catch (EnvoiException e)
            {
                logger.LogError("Réponse de l'agent non envoyée : {Erreur}", e.Message);
            }
        }

        var decision = journal.Ouvrir(declencheur, PolitiqueActions.ReponseConversation, StatutDecision.Executee);
        decision.DemandeurId = demandeur?.Id;
        decision.Conversation = conversation;
        decision.MessageId = message?.Id;
        decision.Motif = question.Length > 500 ? question[..500] : question;
        decision.OutilsJson = JournalDecisions.Json(journalOutils.Outils);
        decision.SourcesJson = JournalDecisions.Json(journalOutils.Sources);
        decision.Resultat = texte.Length > 4000 ? texte[..4000] : texte;
        decision.ActionRetenue = journalOutils.Escalade ? PolitiqueActions.Escalade : PolitiqueActions.ReponseConversation;
        decision.DonneesJson = JournalDecisions.Json(new { moteur });
        decision.TraiteLe = decision.CreeLe;
        await db.SaveChangesAsync(ct);

        return new ReponseAgentDto(conversation.Id, texte, journalOutils.Outils.ToList(), journalOutils.Sources.ToList(),
            journalOutils.Escalade, moteur, decision.Id, journalOutils.PropositionReservationId);
    }

    private async Task<(string, string)> ProduireAsync(Conversation conversation, string question, CancellationToken ct)
    {
        var cle = contexte.Utilisateur is { } u ? $"adherent-{u.Id}" : $"anonyme-{conversation.Telephone ?? conversation.Id.ToString()}";
        if (!limiteur.AccepterMessage(cle))
            return ("Vous avez envoyé beaucoup de messages en peu de temps. Merci de patienter une minute avant de réessayer.", Limite);

        var historique = await HistoriqueAsync(conversation, ct);
        var secours = MoteurSimuleNom;
        if (ClaudeDemande && !ClePresente)
        {
            logger.LogWarning("Agent:Mode=claude mais aucune clé API : moteur simulé utilisé.");
            secours = SecoursSansCle;
        }
        else if (ClaudeDemande)
        {
            if (!limiteur.ConsommerAppelModele())
            {
                logger.LogWarning("Plafond quotidien d'appels au modèle atteint : moteur simulé utilisé.");
                secours = SecoursPlafond;
            }
            else
            {
                try
                {
                    var texte = await moteurClaude.RepondreAsync(question, historique, ct);
                    if (!string.IsNullOrWhiteSpace(texte)) return (texte, MoteurClaudeNom);
                    secours = SecoursPanne;
                }
                catch (RefusModeleException)
                {
                    secours = SecoursRefus;
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // Coupure réseau, quota, clé invalide… : l'agent ne reste pas muet.
                    logger.LogError(e, "API Claude indisponible : repli sur le moteur simulé.");
                    secours = SecoursPanne;
                }
                // Les outils déjà appelés par Claude ne se mélangent pas à ceux du secours.
                journalOutils.Reinitialiser();
            }
        }
        return (await moteurSimule.RepondreAsync(question, historique, ct), secours);
    }

    private async Task<List<EchangeHistorique>> HistoriqueAsync(Conversation conversation, CancellationToken ct)
    {
        if (conversation.Id == 0) return [];
        var messages = await db.Messages.Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreeLe).ThenByDescending(m => m.Id).Take(11).ToListAsync(ct);
        messages.Reverse();
        if (messages.Count > 0 && messages[^1].Sens == Sens.Entrant) messages.RemoveAt(messages.Count - 1); // la question en cours
        return messages.Select(m => new EchangeHistorique(m.Sens == Sens.Entrant, m.Contenu)).ToList();
    }
}
