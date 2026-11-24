using MediaLib.Api.Communication;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Services;

/// <summary>Réservations : file d'attente, mise de côté au retour, expiration du délai de retrait.</summary>
public class ReservationService(
    MediaLibDbContext db,
    EmpruntService emprunts,
    ServiceCommunication communication,
    JournalDecisions journal,
    IOptions<ReglesBibliotheque> regles,
    TimeProvider horloge,
    ILogger<ReservationService> logger) : IObservateurRetour
{
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    public async Task<string?> MotifRefusAsync(Livre livre, string utilisateurId, CancellationToken ct = default)
    {
        if (await db.Reservations.AnyAsync(r => r.LivreId == livre.Id && r.UtilisateurId == utilisateurId
                && (r.Statut == StatutReservation.EnAttente || r.Statut == StatutReservation.Notifiee), ct))
            return "Vous avez déjà une réservation en cours pour ce livre.";
        if (await db.Emprunts.AnyAsync(e => e.LivreId == livre.Id && e.UtilisateurId == utilisateurId && e.DateRetourReelle == null, ct))
            return "Vous avez déjà ce livre en prêt.";
        if (await emprunts.ExemplairesDisponiblesAsync(livre, utilisateurId, ct) > 0)
            return "Ce livre est disponible : inutile de le réserver, vous pouvez l'emprunter.";
        return null;
    }

    public async Task<Reservation> ReserverAsync(int livreId, string utilisateurId, CancellationToken ct = default)
    {
        var livre = await db.Livres.FindAsync([livreId], ct) ?? throw new IntrouvableException($"Livre {livreId} introuvable.");
        if (await MotifRefusAsync(livre, utilisateurId, ct) is { } motif) throw new RegleMetierException(motif);
        var reservation = new Reservation { LivreId = livreId, UtilisateurId = utilisateurId, CreeLe = horloge.Maintenant(), Livre = livre };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task AnnulerAsync(int reservationId, string utilisateurId, CancellationToken ct = default)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId && r.UtilisateurId == utilisateurId, ct)
            ?? throw new IntrouvableException("Réservation introuvable.");
        if (!reservation.EstActive) throw new RegleMetierException("Cette réservation n'est plus active.");
        var etaitMiseDeCote = reservation.Statut == StatutReservation.Notifiee;
        reservation.Statut = StatutReservation.Annulee;
        await db.SaveChangesAsync(ct);
        if (etaitMiseDeCote) await NotifierSuivantsAsync(reservation.LivreId, ct);
    }

    public async Task<int> PositionAsync(Reservation r, CancellationToken ct = default) =>
        r.Statut != StatutReservation.EnAttente ? 0
            : 1 + await db.Reservations.CountAsync(x => x.LivreId == r.LivreId && x.Statut == StatutReservation.EnAttente
                                                         && (x.CreeLe < r.CreeLe || (x.CreeLe == r.CreeLe && x.Id < r.Id)), ct);

    public async Task<List<ReservationDto>> MesReservationsAsync(string utilisateurId, CancellationToken ct = default)
    {
        var liste = await db.Reservations.Include(r => r.Livre).Where(r => r.UtilisateurId == utilisateurId)
            .OrderByDescending(r => r.CreeLe).ToListAsync(ct);
        var resultat = new List<ReservationDto>();
        foreach (var r in liste)
            resultat.Add(new ReservationDto(r.Id, r.LivreId, r.Livre.Titre, r.Statut, r.CreeLe, r.ExpireLe, await PositionAsync(r, ct)));
        return resultat;
    }

    public async Task<int> EnAttenteAsync(int livreId, CancellationToken ct = default) =>
        await db.Reservations.CountAsync(r => r.LivreId == livreId && r.Statut == StatutReservation.EnAttente, ct);

    public Task LivreRenduAsync(Emprunt emprunt, CancellationToken ct) => NotifierSuivantsAsync(emprunt.LivreId, ct);

    /// <summary>Tant qu'un exemplaire est libre, le premier de la file est prévenu et l'exemplaire lui est réservé.</summary>
    public async Task<int> NotifierSuivantsAsync(int livreId, CancellationToken ct = default)
    {
        await Verrou.WaitAsync(ct);
        try
        {
            var livre = await db.Livres.FindAsync([livreId], ct);
            if (livre is null) return 0;
            var notifies = 0;
            while (await emprunts.ExemplairesDisponiblesAsync(livre, null, ct) > 0)
            {
                var suivante = await db.Reservations.Include(r => r.Utilisateur).Include(r => r.Livre)
                    .Where(r => r.LivreId == livreId && r.Statut == StatutReservation.EnAttente)
                    .OrderBy(r => r.CreeLe).ThenBy(r => r.Id).FirstOrDefaultAsync(ct);
                if (suivante is null) break;
                await MettreDeCoteAsync(suivante, ct);
                notifies++;
            }
            return notifies;
        }
        finally
        {
            Verrou.Release();
        }
    }

    public async Task<int> ExpirerReservationsAsync(CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        var expirees = await db.Reservations.Where(r => r.Statut == StatutReservation.Notifiee && r.ExpireLe < maintenant).ToListAsync(ct);
        foreach (var r in expirees) r.Statut = StatutReservation.Expiree;
        await db.SaveChangesAsync(ct);
        foreach (var livreId in expirees.Select(r => r.LivreId).Distinct()) await NotifierSuivantsAsync(livreId, ct);
        return expirees.Count;
    }

    private async Task MettreDeCoteAsync(Reservation reservation, CancellationToken ct)
    {
        var maintenant = horloge.Maintenant();
        var expireLe = maintenant.Date.AddDays(regles.Value.DelaiRetraitReservationJours).AddHours(23).AddMinutes(59);
        reservation.Statut = StatutReservation.Notifiee;
        reservation.NotifieeLe = maintenant;
        reservation.ExpireLe = expireLe;

        var decision = journal.Ouvrir("Retour d'un livre réservé", PolitiqueActions.NotificationReservation);
        decision.DemandeurId = reservation.UtilisateurId;
        decision.Motif = $"« {reservation.Livre.Titre} » est revenu ; {reservation.Utilisateur.NomComplet} est premier dans la file.";
        decision.DonneesJson = JournalDecisions.Json(new { reservation = reservation.Id, livre = reservation.LivreId, expireLe });
        await db.SaveChangesAsync(ct);

        try
        {
            var message = await communication.NotifierAsync(reservation.Utilisateur, ModeleMessage.ReservationDisponible,
                [reservation.Utilisateur.Prenom, reservation.Livre.Titre, expireLe.ToString("dd/MM/yyyy")],
                $"reservation-{reservation.Id}-disponible", ct);
            decision.MessageId = message.Id;
            journal.ChangerStatut(decision, StatutDecision.Executee, $"Adhérent prévenu par {message.Canal}.", PolitiqueActions.NotificationReservation);
        }
        catch (EnvoiException e)
        {
            logger.LogError("Notification de réservation impossible : {Erreur}", e.Message);
            journal.ChangerStatut(decision, StatutDecision.Escaladee, "Échec de la notification : " + e.Message);
        }
        await db.SaveChangesAsync(ct);
    }
}
