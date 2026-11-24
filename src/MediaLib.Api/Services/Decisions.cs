using MediaLib.Api.Communication;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Services;

/// <summary>Demandes préparées pour un adhérent (prolongation à valider, réservation à confirmer).</summary>
public class DemandesAdherent(
    MediaLibDbContext db,
    EmpruntService emprunts,
    ReservationService reservations,
    JournalDecisions journal)
{
    public async Task<DecisionAgent> DemanderProlongationAsync(int empruntId, string utilisateurId, string declencheur, Conversation? conversation = null, CancellationToken ct = default)
    {
        // L'identité vient toujours du serveur : un emprunt d'un autre adhérent est « introuvable ».
        var emprunt = await db.Emprunts.Include(e => e.Livre).Include(e => e.Utilisateur)
            .FirstOrDefaultAsync(e => e.Id == empruntId && e.UtilisateurId == utilisateurId, ct)
            ?? throw new IntrouvableException("Emprunt introuvable parmi vos emprunts.");
        if (await emprunts.MotifRefusProlongationAsync(emprunt, ct) is { } motif) throw new RegleMetierException(motif);
        if (await ProlongationEnAttenteAsync(emprunt.Id, ct) is { } existante) return existante;

        var nouvelleEcheance = emprunt.DateRetourPrevue.AddDays(emprunts.Regles.ProlongationJours);
        var decision = journal.Ouvrir(declencheur, PolitiqueActions.Prolongation);
        decision.DemandeurId = utilisateurId;
        decision.EmpruntId = emprunt.Id;
        decision.Conversation = conversation;
        decision.Motif = $"Prolongation de « {emprunt.Livre.Titre} » demandée par {emprunt.Utilisateur.NomComplet}.";
        decision.DonneesJson = JournalDecisions.Json(new { echeanceActuelle = emprunt.DateRetourPrevue, nouvelleEcheance });
        await db.SaveChangesAsync(ct);
        return decision;
    }

    public Task<DecisionAgent?> ProlongationEnAttenteAsync(int empruntId, CancellationToken ct = default) =>
        db.Decisions.FirstOrDefaultAsync(d => d.EmpruntId == empruntId && d.ActionProposee == PolitiqueActions.Prolongation
                                              && d.Statut == StatutDecision.EnAttenteValidation, ct);

    public async Task<DecisionAgent> ProposerReservationAsync(int livreId, string utilisateurId, string declencheur, Conversation? conversation = null, CancellationToken ct = default)
    {
        var livre = await db.Livres.FindAsync([livreId], ct) ?? throw new IntrouvableException("Livre introuvable.");
        if (await reservations.MotifRefusAsync(livre, utilisateurId, ct) is { } motif) throw new RegleMetierException(motif);
        var decision = journal.Ouvrir(declencheur, PolitiqueActions.Reservation);
        decision.DemandeurId = utilisateurId;
        decision.Conversation = conversation;
        decision.Motif = $"Réservation de « {livre.Titre} » proposée à l'adhérent (à confirmer par lui).";
        decision.DonneesJson = JournalDecisions.Json(new ProposalReservation(livre.Id));
        await db.SaveChangesAsync(ct);
        return decision;
    }

    public async Task<Reservation> ConfirmerReservationAsync(int decisionId, string utilisateurId, CancellationToken ct = default)
    {
        var proposition = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId, ct);
        if (proposition is null || proposition.ActionProposee != PolitiqueActions.Reservation || proposition.DemandeurId != utilisateurId)
            throw new IntrouvableException("Proposition de réservation introuvable.");
        if (proposition.Statut != StatutDecision.Proposee) throw new RegleMetierException("Cette proposition a déjà été traitée.");
        var livreId = JournalDecisions.Lire<ProposalReservation>(proposition.DonneesJson)?.Livre
            ?? throw new RegleMetierException("Proposition incomplète.");
        var reservation = await reservations.ReserverAsync(livreId, utilisateurId, ct);
        journal.ChangerStatut(proposition, StatutDecision.Executee, "Réservation confirmée par l'adhérent.", PolitiqueActions.Reservation);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public Task<DecisionAgent?> PropositionEnAttenteAsync(string utilisateurId, DateTime depuis, CancellationToken ct = default) =>
        db.Decisions.Where(d => d.DemandeurId == utilisateurId && d.ActionProposee == PolitiqueActions.Reservation
                                && d.Statut == StatutDecision.Proposee && d.CreeLe >= depuis)
            .OrderByDescending(d => d.CreeLe).FirstOrDefaultAsync(ct);

    public record ProposalReservation(int Livre);
}

/// <summary>Validation humaine des actions sensibles par un bibliothécaire.</summary>
public class ValidationDecisions(
    MediaLibDbContext db,
    JournalDecisions journal,
    RelanceService relances,
    EmpruntService emprunts,
    ServiceCommunication communication,
    ILogger<ValidationDecisions> logger)
{
    public async Task<DecisionAgent> ChargerAsync(int id, CancellationToken ct = default) =>
        await db.Decisions.Include(d => d.Demandeur).Include(d => d.ValidePar).Include(d => d.Conversation)
            .FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new IntrouvableException($"Décision {id} introuvable.");

    public async Task<DecisionAgent> ApprouverAsync(int id, string bibliothecaireId, string? commentaire, CancellationToken ct = default)
    {
        var decision = await ChargerAsync(id, ct);
        journal.Valider(decision, bibliothecaireId, true, commentaire);
        await db.SaveChangesAsync(ct);
        try
        {
            switch (decision.ActionProposee)
            {
                case var a when a.StartsWith("relance.", StringComparison.Ordinal) && a != PolitiqueActions.RelanceManuelle:
                    var relance = await relances.PourDecisionAsync(decision.Id, ct) ?? throw new IntrouvableException("Relance introuvable.");
                    await relances.ApprouverAsync(relance, ct);
                    break;
                case PolitiqueActions.Prolongation:
                    await ApprouverProlongationAsync(decision, ct);
                    break;
                case PolitiqueActions.MessageLibre or PolitiqueActions.RelanceManuelle:
                    await EnvoyerMessageLibreAsync(decision, ct);
                    break;
                default:
                    journal.ChangerStatut(decision, StatutDecision.Executee, commentaire ?? "Cas traité par le bibliothécaire.");
                    break;
            }
        }
        catch (Exception e) when (e is RegleMetierException or EnvoiException or IntrouvableException)
        {
            journal.ChangerStatut(decision, StatutDecision.Escaladee, "Exécution impossible : " + e.Message);
            await db.SaveChangesAsync(ct);
            throw new RegleMetierException(e.Message);
        }
        await db.SaveChangesAsync(ct);
        await db.Entry(decision).Reference(d => d.ValidePar).LoadAsync(ct);
        return decision;
    }

    public async Task<DecisionAgent> RejeterAsync(int id, string bibliothecaireId, string? commentaire, CancellationToken ct = default)
    {
        var decision = await ChargerAsync(id, ct);
        journal.Valider(decision, bibliothecaireId, false, commentaire);
        if (await relances.PourDecisionAsync(decision.Id, ct) is { } relance)
            await relances.AnnulerAsync(relance, commentaire ?? "Relance rejetée par le bibliothécaire.", StatutDecision.Rejetee, ct);

        // L'adhérent qui attendait une prolongation est informé sur le canal de sa demande.
        if (decision.ActionProposee == PolitiqueActions.Prolongation && decision.Conversation is { } conversation)
        {
            try
            {
                await communication.RepondreAsync(conversation,
                    "Votre demande de prolongation n'a pas été acceptée" + (commentaire is null ? "." : " : " + commentaire), Message.AuteurBibliothecaire, ct);
            }
            catch (EnvoiException e)
            {
                logger.LogWarning("Information de refus non envoyée : {Erreur}", e.Message);
            }
        }
        await db.SaveChangesAsync(ct);
        await db.Entry(decision).Reference(d => d.ValidePar).LoadAsync(ct);
        return decision;
    }

    private async Task ApprouverProlongationAsync(DecisionAgent decision, CancellationToken ct)
    {
        var emprunt = await db.Emprunts.Include(e => e.Livre).FirstOrDefaultAsync(e => e.Id == decision.EmpruntId, ct)
            ?? throw new IntrouvableException("Emprunt introuvable.");
        await emprunts.ProlongerAsync(emprunt, ct);
        journal.ChangerStatut(decision, StatutDecision.Executee, $"Nouvelle échéance : {emprunt.DateRetourPrevue:dd/MM/yyyy}", PolitiqueActions.Prolongation);
        if (decision.Conversation is { } conversation)
        {
            try
            {
                await communication.RepondreAsync(conversation,
                    $"Votre prolongation est accordée : « {emprunt.Livre.Titre} » est à rendre le {emprunt.DateRetourPrevue:dd/MM/yyyy}.", Message.AuteurBibliothecaire, ct);
            }
            catch (EnvoiException e)
            {
                logger.LogWarning("Confirmation de prolongation non envoyée : {Erreur}", e.Message);
            }
        }
    }

    private async Task EnvoyerMessageLibreAsync(DecisionAgent decision, CancellationToken ct)
    {
        var donnees = JournalDecisions.Lire<MessagePrepare>(decision.DonneesJson);
        if (donnees is null || string.IsNullOrWhiteSpace(donnees.Texte)) throw new RegleMetierException("Message préparé introuvable.");
        Message message;
        if (decision.Conversation is { } conversation)
        {
            message = await communication.RepondreAsync(conversation, donnees.Texte, Message.AuteurAgent, ct);
            if (conversation.Etat == EtatConversation.Humain) conversation.Etat = EtatConversation.Agent;
        }
        else
        {
            var u = await db.Users.FindAsync([decision.DemandeurId], ct) ?? throw new IntrouvableException("Adhérent introuvable.");
            var conv = await communication.ConversationPourAsync(u, Canal.Email, ct);
            message = await communication.RepondreAsync(conv, donnees.Texte, Message.AuteurBibliothecaire, ct);
        }
        decision.MessageId = message.Id;
        journal.ChangerStatut(decision, StatutDecision.Executee, "Message envoyé.", decision.ActionProposee);
    }

    public record MessagePrepare(string Texte);
}
