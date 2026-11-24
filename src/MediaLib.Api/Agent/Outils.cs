using System.Text.Json;
using MediaLib.Api.Connaissance;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Agent;

/// <summary>
/// Qui parle à l'agent ? Rempli par le serveur (JWT ou numéro WhatsApp vérifié), jamais par le modèle :
/// aucun outil n'accepte d'identifiant d'adhérent fourni librement.
/// </summary>
public class ContexteDemandeur
{
    public Utilisateur? Utilisateur { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public Canal Canal { get; private set; } = Canal.Web;
    public Conversation? Conversation { get; private set; }
    public string Declencheur { get; private set; } = "Assistant";

    public bool EstIdentifie => Utilisateur is not null;
    public bool EstBibliothecaire => Roles.Contains(Shared.Roles.Bibliothecaire) || Roles.Contains(Shared.Roles.Admin);

    public void Definir(Utilisateur? utilisateur, IReadOnlyList<string> roles, Conversation conversation, string declencheur)
    {
        Utilisateur = utilisateur;
        Roles = utilisateur is null ? [] : roles;
        Canal = conversation.Canal;
        Conversation = conversation;
        Declencheur = declencheur;
    }
}

/// <summary>Trace des outils appelés pendant une réponse (pour le journal des décisions).</summary>
public class JournalOutils
{
    private readonly List<string> _outils = [];
    private readonly List<SourceDto> _sources = [];

    public IReadOnlyList<string> Outils => _outils;
    public IReadOnlyList<SourceDto> Sources => _sources;
    public bool Escalade { get; set; }
    public int? PropositionReservationId { get; set; }
    public int? DecisionPrepareeId { get; set; }

    public void Outil(string nom)
    {
        if (!_outils.Contains(nom)) _outils.Add(nom);
    }

    public void Source(SourceDto s)
    {
        if (!_sources.Contains(s)) _sources.Add(s);
    }

    public void Reinitialiser()
    {
        _outils.Clear();
        _sources.Clear();
        Escalade = false;
        PropositionReservationId = null;
        DecisionPrepareeId = null;
    }
}

public record LivreTrouve(int Id, string Titre, string Auteur, int Annee, string Categorie, int Disponibles);

public record ResultatRecherche(int Total, List<LivreTrouve> Livres, string? Message = null);

/// <summary>Outils de l'agent bibliothécaire, partagés par le moteur simulé et le moteur Claude.</summary>
public class OutilsAgent(
    MediaLibDbContext db,
    ContexteDemandeur contexte,
    JournalOutils journalOutils,
    JournalDecisions journal,
    PolitiqueActions politique,
    EmpruntService emprunts,
    ReservationService reservations,
    DemandesAdherent demandes,
    RechercheConnaissance rag,
    TimeProvider horloge)
{
    // ----------------------------------------------------------------- catalogue

    public async Task<ResultatRecherche> RechercherLivresAsync(string texte, string? categorie = null, bool disponibleSeulement = false, CancellationToken ct = default)
    {
        journalOutils.Outil("rechercherLivres");
        var q = db.Livres.AsNoTracking().Include(l => l.Auteur).Include(l => l.Categorie).AsQueryable();
        if (!string.IsNullOrWhiteSpace(categorie)) q = q.Where(l => EF.Functions.Like(l.Categorie.Nom, $"%{categorie.Trim()}%"));
        var candidats = await q.ToListAsync(ct);

        var mots = Normaliseur.MotsSignificatifs(texte);
        var phrase = Normaliseur.SansAccents(texte.Trim());
        var classes = candidats.Select(l =>
            {
                var cible = Normaliseur.SansAccents($"{l.Titre} {l.Auteur.Prenom} {l.Auteur.Nom} {l.Categorie.Nom}");
                var score = mots.Count(m => cible.Contains(m)) + (phrase.Length > 3 && cible.Contains(phrase) ? 5 : 0);
                return (Livre: l, Score: score);
            })
            .Where(x => mots.Count == 0 ? !string.IsNullOrWhiteSpace(categorie) : x.Score > 0)
            .ToList();
        if (classes.Count > 0)
        {
            var meilleur = classes.Max(x => x.Score);
            // On garde les meilleurs correspondants pour éviter le bruit d'un seul mot commun.
            classes = classes.Where(x => x.Score >= Math.Max(1, meilleur - 1)).ToList();
        }
        var tous = classes.OrderByDescending(x => x.Score).ThenBy(x => x.Livre.Titre).Select(x => x.Livre).ToList();
        var filtres = disponibleSeulement ? tous.Where(l => l.NbDisponibles > 0).ToList() : tous;
        var livres = filtres.Take(8).Select(l => new LivreTrouve(l.Id, l.Titre, l.Auteur.NomComplet, l.Annee, l.Categorie.Nom, l.NbDisponibles)).ToList();
        var message = disponibleSeulement && tous.Count > 0 && filtres.Count == 0 ? "Des livres correspondent mais aucun exemplaire n'est disponible." : null;
        return new ResultatRecherche(filtres.Count, livres, message);
    }

    public async Task<object> VoirDisponibiliteAsync(int livreId, CancellationToken ct = default)
    {
        journalOutils.Outil("voirDisponibilite");
        var livre = await db.Livres.Include(l => l.Auteur).FirstOrDefaultAsync(l => l.Id == livreId, ct);
        if (livre is null) return new { erreur = "Livre absent du catalogue." };
        var prochainRetour = await db.Emprunts.Where(e => e.LivreId == livreId && e.DateRetourReelle == null)
            .OrderBy(e => e.DateRetourPrevue).Select(e => (DateOnly?)e.DateRetourPrevue).FirstOrDefaultAsync(ct);
        return new
        {
            livre.Id,
            livre.Titre,
            auteur = livre.Auteur.NomComplet,
            exemplaires = livre.NbExemplaires,
            disponibles = await emprunts.ExemplairesDisponiblesAsync(livre, contexte.Utilisateur?.Id, ct),
            prochainRetourPrevu = prochainRetour?.ToString("dd/MM/yyyy"),
            reservationsEnAttente = await reservations.EnAttenteAsync(livreId, ct),
        };
    }

    public async Task<ResultatRecherche> RecommanderLivresAsync(int? livreId, string? texte = null, CancellationToken ct = default)
    {
        journalOutils.Outil("recommanderLivres");
        var q = db.Livres.AsNoTracking().Include(l => l.Auteur).Include(l => l.Categorie).AsQueryable();
        if (livreId is { } id && await db.Livres.FindAsync([id], ct) is { } reference)
            q = q.Where(l => l.Id != id && (l.CategorieId == reference.CategorieId || l.AuteurId == reference.AuteurId));
        else if (!string.IsNullOrWhiteSpace(texte))
        {
            var mots = Normaliseur.MotsSignificatifs(texte);
            var categories = (await db.Categories.ToListAsync(ct))
                .Where(c => mots.Any(m => Normaliseur.SansAccents(c.Nom).Contains(m))).Select(c => c.Id).ToList();
            if (categories.Count > 0) q = q.Where(l => categories.Contains(l.CategorieId));
        }
        var livres = await q.OrderByDescending(l => l.NbDisponibles).ThenByDescending(l => l.Annee).Take(5).ToListAsync(ct);
        return new ResultatRecherche(livres.Count, livres.Select(l => new LivreTrouve(l.Id, l.Titre, l.Auteur.NomComplet, l.Annee, l.Categorie.Nom, l.NbDisponibles)).ToList());
    }

    // ----------------------------------------------------------------- règlement (RAG)

    public async Task<List<ExtraitDto>> ChercherReglementAsync(string question, CancellationToken ct = default)
    {
        journalOutils.Outil("chercherReglement");
        var extraits = await rag.RechercherAsync(question, 3, ct);
        foreach (var e in extraits) journalOutils.Source(new SourceDto(e.Titre, e.Version, e.Section));
        return extraits;
    }

    // ----------------------------------------------------------------- données personnelles (identité serveur)

    public async Task<object> VoirMesEmpruntsAsync(CancellationToken ct = default)
    {
        journalOutils.Outil("voirMesEmprunts");
        if (contexte.Utilisateur is not { } u) return new { erreur = "Demandeur non identifié : connexion requise." };
        var aujourdhui = horloge.Aujourdhui();
        var enCours = await emprunts.MesEmpruntsAsync(u.Id, historique: false, ct);
        var mesReservations = (await reservations.MesReservationsAsync(u.Id, ct)).Where(r => r.Statut is StatutReservation.EnAttente or StatutReservation.Notifiee).ToList();
        return new
        {
            adherent = u.Prenom,
            empruntsEnCours = enCours.Select(e => new
            {
                id = e.Id,
                livre = e.Livre.Titre,
                aRendreLe = e.DateRetourPrevue.ToString("dd/MM/yyyy"),
                joursRestants = e.DateRetourPrevue.DayNumber - aujourdhui.DayNumber,
                prolongations = e.NbProlongations,
            }).ToList(),
            empruntsRestantsAutorises = Math.Max(0, emprunts.Regles.MaxEmprunts - enCours.Count),
            reservations = mesReservations.Select(r => new { livre = r.LivreTitre, statut = r.Statut.ToString(), aRetirerAvantLe = r.ExpireLe?.ToString("dd/MM/yyyy"), position = r.Position }).ToList(),
        };
    }

    public async Task<object> PreparerReservationAsync(int livreId, CancellationToken ct = default)
    {
        journalOutils.Outil("preparerReservation");
        if (contexte.Utilisateur is not { } u) return new { possible = false, motif = "Connexion requise pour réserver." };
        try
        {
            var d = await demandes.ProposerReservationAsync(livreId, u.Id, contexte.Declencheur, contexte.Conversation, ct);
            journalOutils.PropositionReservationId = d.Id;
            return new { possible = true, propositionId = d.Id, personnesDejaEnAttente = await reservations.EnAttenteAsync(livreId, ct), confirmation = "L'adhérent doit confirmer la réservation." };
        }
        catch (Exception e) when (e is RegleMetierException or IntrouvableException)
        {
            return new { possible = false, motif = e.Message };
        }
    }

    public async Task<object> PreparerProlongationAsync(int empruntId, CancellationToken ct = default)
    {
        journalOutils.Outil("preparerProlongation");
        if (contexte.Utilisateur is not { } u) return new { possible = false, motif = "Connexion requise." };
        try
        {
            var existante = await db.Decisions.AnyAsync(d => d.EmpruntId == empruntId && d.DemandeurId == u.Id
                && d.ActionProposee == PolitiqueActions.Prolongation && d.Statut == StatutDecision.EnAttenteValidation, ct);
            var d = await demandes.DemanderProlongationAsync(empruntId, u.Id, contexte.Declencheur, contexte.Conversation, ct);
            journalOutils.DecisionPrepareeId = d.Id;
            var donnees = JsonDocument.Parse(d.DonneesJson).RootElement;
            return new
            {
                possible = true,
                dejaDemandee = existante,
                validationRequise = politique.Classer(PolitiqueActions.Prolongation) == Classement.ValidationRequise,
                nouvelleEcheanceSiAcceptee = DateOnly.Parse(donnees.GetProperty("nouvelleEcheance").GetString()!).ToString("dd/MM/yyyy"),
            };
        }
        catch (Exception e) when (e is RegleMetierException or IntrouvableException)
        {
            return new { possible = false, motif = e.Message };
        }
    }

    /// <summary>Bibliothécaire uniquement : prépare une relance personnalisée, envoyée après validation.</summary>
    public async Task<object> PreparerRelanceAsync(int empruntId, string? texte, CancellationToken ct = default)
    {
        journalOutils.Outil("preparerRelance");
        if (!contexte.EstBibliothecaire) return new { possible = false, motif = "Action réservée aux bibliothécaires." };
        var e = await db.Emprunts.Include(x => x.Livre).Include(x => x.Utilisateur).FirstOrDefaultAsync(x => x.Id == empruntId, ct);
        if (e is null || !e.EstEnCours) return new { possible = false, motif = "Emprunt introuvable ou déjà rendu." };
        var contenu = string.IsNullOrWhiteSpace(texte)
            ? $"Bonjour {e.Utilisateur.Prenom}, le livre « {e.Livre.Titre} » était à rendre le {e.DateRetourPrevue:dd/MM/yyyy}. Merci de le rapporter ou de nous contacter."
            : texte.Trim();
        var d = journal.Ouvrir(contexte.Declencheur, PolitiqueActions.RelanceManuelle);
        d.DemandeurId = e.UtilisateurId;
        d.EmpruntId = e.Id;
        d.Motif = $"Relance manuelle préparée par l'agent pour « {e.Livre.Titre} ».";
        d.DonneesJson = JournalDecisions.Json(new ValidationDecisions.MessagePrepare(contenu));
        await db.SaveChangesAsync(ct);
        journalOutils.DecisionPrepareeId = d.Id;
        return new { possible = true, decisionId = d.Id, statut = "en attente de validation", texte = contenu };
    }

    public async Task<object> TransmettreAuBibliothecaireAsync(string motif, string resume, CancellationToken ct = default)
    {
        journalOutils.Outil("transmettreAuBibliothecaire");
        var d = journal.Ouvrir(contexte.Declencheur, PolitiqueActions.Escalade, StatutDecision.Escaladee);
        d.DemandeurId = contexte.Utilisateur?.Id;
        d.Conversation = contexte.Conversation;
        d.Motif = $"{motif} : {resume}";
        d.Resultat = "Conversation transmise à un bibliothécaire.";
        if (contexte.Conversation is { } c) c.Etat = EtatConversation.Humain;
        await db.SaveChangesAsync(ct);
        journalOutils.Escalade = true;
        return new { transmis = true, decisionId = d.Id };
    }

    /// <summary>
    /// Résumé d'un adhérent. Un membre n'obtient que le sien ; un bibliothécaire peut désigner un adhérent par e-mail,
    /// ou obtenir la situation du jour (retards, décisions en attente).
    /// </summary>
    public async Task<string> ResumerAdherentAsync(string? emailAdherent, CancellationToken ct = default)
    {
        journalOutils.Outil("resumerAdherent");
        if (contexte.Utilisateur is null) return "Connexion requise.";
        Utilisateur? cible = contexte.Utilisateur;
        if (contexte.EstBibliothecaire)
        {
            if (string.IsNullOrWhiteSpace(emailAdherent)) return await SituationDuJourAsync(ct);
            cible = await db.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == emailAdherent.Trim().ToUpperInvariant(), ct);
            if (cible is null) return "Aucun adhérent avec cet e-mail.";
        }
        else if (!string.IsNullOrWhiteSpace(emailAdherent) && !string.Equals(emailAdherent.Trim(), contexte.Utilisateur.Email, StringComparison.OrdinalIgnoreCase))
            return "Je ne peux communiquer aucune information sur le compte d'une autre personne.";

        var aujourdhui = horloge.Aujourdhui();
        var enCours = await emprunts.MesEmpruntsAsync(cible.Id, false, ct);
        var rendus = await db.Emprunts.CountAsync(e => e.UtilisateurId == cible.Id && e.DateRetourReelle != null, ct);
        var retards = enCours.Count(e => e.EstEnRetard(aujourdhui));
        var resas = await db.Reservations.CountAsync(r => r.UtilisateurId == cible.Id && (r.Statut == StatutReservation.EnAttente || r.Statut == StatutReservation.Notifiee), ct);
        var lignes = enCours.Select(e => $"- « {e.Livre.Titre} » à rendre le {e.DateRetourPrevue:dd/MM/yyyy}{(e.EstEnRetard(aujourdhui) ? $" (retard {e.JoursRetard(aujourdhui)} j)" : "")}");
        return $"{cible.NomComplet} : {enCours.Count} emprunt(s) en cours dont {retards} en retard, {rendus} livre(s) déjà rendu(s), {resas} réservation(s) active(s)."
               + (enCours.Count > 0 ? "\n" + string.Join('\n', lignes) : string.Empty)
               + $"\nCanal préféré : {ServiceCanal(cible)}.";
    }

    private static string ServiceCanal(Utilisateur u) =>
        Communication.ServiceCommunication.CanalPour(u) == Canal.WhatsApp ? "WhatsApp (numéro vérifié, consentement donné)" : "e-mail";

    private async Task<string> SituationDuJourAsync(CancellationToken ct)
    {
        var retards = await emprunts.EnRetardAsync(ct);
        var attente = await db.Decisions.CountAsync(d => d.Statut == StatutDecision.EnAttenteValidation, ct);
        var escalades = await db.Conversations.CountAsync(c => c.Etat == EtatConversation.Humain, ct);
        var aujourdhui = horloge.Aujourdhui();
        var lignes = retards.Take(10).Select(e => $"- {e.Utilisateur.NomComplet} : « {e.Livre.Titre} », {e.JoursRetard(aujourdhui)} jour(s) de retard");
        return $"Situation du {aujourdhui:dd/MM/yyyy} : {retards.Count} emprunt(s) en retard, {attente} décision(s) à valider, {escalades} conversation(s) à reprendre."
               + (retards.Count > 0 ? "\n" + string.Join('\n', lignes) : string.Empty);
    }

    // ----------------------------------------------------------------- point d'entrée générique (moteur Claude)

    /// <summary>Exécute un outil par son nom. Un nom inconnu est une action interdite par défaut (journalisée).</summary>
    public async Task<string> ExecuterAsync(string nom, JsonElement entree, CancellationToken ct = default)
    {
        string? S(string p) => entree.ValueKind == JsonValueKind.Object && entree.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? I(string p) => entree.ValueKind == JsonValueKind.Object && entree.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        bool B(string p) => entree.ValueKind == JsonValueKind.Object && entree.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

        object resultat = nom switch
        {
            "rechercherLivres" => await RechercherLivresAsync(S("texte") ?? string.Empty, S("categorie"), B("disponibleSeulement"), ct),
            "voirDisponibilite" => await VoirDisponibiliteAsync(I("livreId") ?? 0, ct),
            "recommanderLivres" => await RecommanderLivresAsync(I("livreId"), S("texte"), ct),
            "chercherReglement" => await ChercherReglementAsync(S("question") ?? string.Empty, ct),
            "voirMesEmprunts" => await VoirMesEmpruntsAsync(ct),
            "preparerReservation" => await PreparerReservationAsync(I("livreId") ?? 0, ct),
            "preparerProlongation" => await PreparerProlongationAsync(I("empruntId") ?? 0, ct),
            "preparerRelance" => await PreparerRelanceAsync(I("empruntId") ?? 0, S("texte"), ct),
            "transmettreAuBibliothecaire" => await TransmettreAuBibliothecaireAsync(S("motif") ?? "demande", S("resume") ?? string.Empty, ct),
            "resumerAdherent" => await ResumerAdherentAsync(S("email"), ct),
            _ => await RefuserAsync(nom, ct),
        };
        return JsonSerializer.Serialize(resultat, MediaLibJson.Options);
    }

    private async Task<object> RefuserAsync(string action, CancellationToken ct)
    {
        var d = journal.Ouvrir(contexte.Declencheur, action);   // classement Interdite par défaut
        d.DemandeurId = contexte.Utilisateur?.Id;
        d.Motif = $"Outil inconnu demandé par le modèle : « {action} ».";
        d.Resultat = "Refusé par la politique des actions.";
        await db.SaveChangesAsync(ct);
        return new { erreur = $"Action « {action} » interdite à l'agent." };
    }
}
