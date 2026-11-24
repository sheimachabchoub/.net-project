using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Services;

/// <summary>Réagit au retour d'un livre (réservations à prévenir, relances à annuler).</summary>
public interface IObservateurRetour
{
    Task LivreRenduAsync(Emprunt emprunt, CancellationToken ct);
}

/// <summary>Emprunts : max 5 en cours, 21 jours, disponibilité, retours, prolongations, retards.</summary>
public class EmpruntService(
    MediaLibDbContext db,
    IOptions<ReglesBibliotheque> regles,
    TimeProvider horloge,
    IServiceProvider services,
    ILogger<EmpruntService> logger)
{
    public ReglesBibliotheque Regles => regles.Value;

    public DateOnly Aujourdhui() => horloge.Aujourdhui();

    public EmpruntDto Dto(Emprunt e) => e.ToDto(Aujourdhui(), Regles.JoursAvantEcheance);

    /// <summary>Exemplaires disponibles pour un adhérent : ceux en rayon moins ceux mis de côté pour d'autres.</summary>
    public async Task<int> ExemplairesDisponiblesAsync(Livre livre, string? pourUtilisateurId = null, CancellationToken ct = default)
    {
        var misDeCote = await db.Reservations.CountAsync(r =>
            r.LivreId == livre.Id && r.Statut == StatutReservation.Notifiee && r.UtilisateurId != pourUtilisateurId, ct);
        return Math.Max(0, livre.NbDisponibles - misDeCote);
    }

    public async Task<int> EmpruntsEnCoursAsync(string utilisateurId, CancellationToken ct = default) =>
        await db.Emprunts.CountAsync(e => e.UtilisateurId == utilisateurId && e.DateRetourReelle == null, ct);

    public async Task<Emprunt> EmprunterAsync(int livreId, string utilisateurId, CancellationToken ct = default)
    {
        var livre = await db.Livres.FirstOrDefaultAsync(l => l.Id == livreId, ct)
            ?? throw new IntrouvableException($"Livre {livreId} introuvable.");
        if (!await db.Users.AnyAsync(u => u.Id == utilisateurId, ct))
            throw new IntrouvableException("Adhérent introuvable.");

        if (await db.Emprunts.AnyAsync(e => e.LivreId == livreId && e.UtilisateurId == utilisateurId && e.DateRetourReelle == null, ct))
            throw new RegleMetierException($"Vous avez déjà « {livre.Titre} » en prêt.");
        if (await ExemplairesDisponiblesAsync(livre, utilisateurId, ct) <= 0)
            throw new RegleMetierException($"Aucun exemplaire de « {livre.Titre} » n'est disponible.");
        if (await EmpruntsEnCoursAsync(utilisateurId, ct) >= Regles.MaxEmprunts)
            throw new RegleMetierException($"Maximum atteint : {Regles.MaxEmprunts} emprunts en cours.");

        var aujourdhui = Aujourdhui();
        var emprunt = new Emprunt
        {
            LivreId = livre.Id,
            UtilisateurId = utilisateurId,
            DateEmprunt = aujourdhui,
            DateRetourPrevue = aujourdhui.AddDays(Regles.DureeJours),
        };
        db.Emprunts.Add(emprunt);
        livre.NbDisponibles--;
        livre.Version = Guid.NewGuid();

        // L'adhérent vient chercher le livre qu'il avait réservé : sa réservation est honorée.
        var reservation = await db.Reservations.FirstOrDefaultAsync(r =>
            r.LivreId == livreId && r.UtilisateurId == utilisateurId
            && (r.Statut == StatutReservation.EnAttente || r.Statut == StatutReservation.Notifiee), ct);
        if (reservation is not null) reservation.Statut = StatutReservation.Honoree;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un autre prêt du même livre a été enregistré au même instant.
            throw new RegleMetierException("Le livre vient d'être emprunté par quelqu'un d'autre. Réessayez.");
        }
        logger.LogInformation("Emprunt {Id} : livre {Livre} pour {Utilisateur}", emprunt.Id, livreId, utilisateurId);
        await db.Entry(emprunt).Reference(e => e.Livre).LoadAsync(ct);
        await db.Entry(emprunt).Reference(e => e.Utilisateur).LoadAsync(ct);
        return emprunt;
    }

    public async Task<Emprunt> EnregistrerRetourAsync(int empruntId, CancellationToken ct = default)
    {
        var emprunt = await db.Emprunts.Include(e => e.Livre).Include(e => e.Utilisateur)
            .FirstOrDefaultAsync(e => e.Id == empruntId, ct) ?? throw new IntrouvableException($"Emprunt {empruntId} introuvable.");
        if (!emprunt.EstEnCours) throw new RegleMetierException("Ce livre a déjà été rendu.");

        emprunt.DateRetourReelle = Aujourdhui();
        emprunt.Livre.NbDisponibles = Math.Min(emprunt.Livre.NbExemplaires, emprunt.Livre.NbDisponibles + 1);
        emprunt.Livre.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        foreach (var observateur in services.GetServices<IObservateurRetour>())
            await observateur.LivreRenduAsync(emprunt, ct);
        return emprunt;
    }

    public async Task<string?> MotifRefusProlongationAsync(Emprunt emprunt, CancellationToken ct = default)
    {
        if (!emprunt.EstEnCours) return "Ce livre a déjà été rendu.";
        if (emprunt.EstEnRetard(Aujourdhui())) return "Un emprunt en retard ne peut pas être prolongé.";
        if (emprunt.NbProlongations >= Regles.ProlongationsMax) return $"Cet emprunt a déjà été prolongé ({Regles.ProlongationsMax} fois maximum).";
        if (await db.Reservations.AnyAsync(r => r.LivreId == emprunt.LivreId && r.Statut == StatutReservation.EnAttente, ct))
            return "Ce livre est réservé par un autre adhérent.";
        return null;
    }

    public async Task ProlongerAsync(Emprunt emprunt, CancellationToken ct = default)
    {
        if (await MotifRefusProlongationAsync(emprunt, ct) is { } motif) throw new RegleMetierException(motif);
        emprunt.DateRetourPrevue = emprunt.DateRetourPrevue.AddDays(Regles.ProlongationJours);
        emprunt.NbProlongations++;
        await db.SaveChangesAsync(ct);
    }

    public IQueryable<Emprunt> Requete() => db.Emprunts.Include(e => e.Livre).Include(e => e.Utilisateur);

    public async Task<List<Emprunt>> MesEmpruntsAsync(string utilisateurId, bool historique, CancellationToken ct = default)
    {
        var q = Requete().Where(e => e.UtilisateurId == utilisateurId);
        q = historique ? q.Where(e => e.DateRetourReelle != null) : q.Where(e => e.DateRetourReelle == null);
        return await q.OrderByDescending(e => e.DateEmprunt).ThenByDescending(e => e.Id).ToListAsync(ct);
    }

    public async Task<List<Emprunt>> EnRetardAsync(CancellationToken ct = default)
    {
        var aujourdhui = Aujourdhui();
        return await Requete().Where(e => e.DateRetourReelle == null && e.DateRetourPrevue < aujourdhui)
            .OrderBy(e => e.DateRetourPrevue).ToListAsync(ct);
    }
}
