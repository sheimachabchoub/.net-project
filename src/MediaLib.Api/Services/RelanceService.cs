using MediaLib.Api.Communication;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Services;

/// <summary>
/// Relances : rappel J-2, relance J+1 (automatiques), relance J+7 (validation humaine).
/// Idempotence par clé unique, réessai borné, annulation si le livre est rendu ou l'échéance modifiée.
/// </summary>
public class RelanceService(
    MediaLibDbContext db,
    PolitiqueActions politique,
    JournalDecisions journal,
    ServiceCommunication communication,
    IOptions<ReglesBibliotheque> regles,
    TimeProvider horloge,
    ILogger<RelanceService> logger) : IObservateurRetour
{
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    /// <summary>Détecte les emprunts concernés et crée les relances manquantes (jamais de doublon).</summary>
    public async Task<Dictionary<NiveauRelance, int>> DetecterAsync(CancellationToken ct = default)
    {
        await Verrou.WaitAsync(ct);
        try
        {
            var r = regles.Value;
            var aujourdhui = horloge.Aujourdhui();
            var enCours = db.Emprunts.Include(e => e.Livre).Include(e => e.Utilisateur).Where(e => e.DateRetourReelle == null);
            var candidats = new Dictionary<NiveauRelance, List<Emprunt>>
            {
                [NiveauRelance.RappelAvantEcheance] = await enCours
                    .Where(e => e.DateRetourPrevue >= aujourdhui && e.DateRetourPrevue <= aujourdhui.AddDays(r.JoursAvantEcheance)).ToListAsync(ct),
                [NiveauRelance.RelanceRetardJ1] = await enCours
                    .Where(e => e.DateRetourPrevue <= aujourdhui.AddDays(-r.RelanceRetardJours) && e.DateRetourPrevue > aujourdhui.AddDays(-r.RelanceValidationJours)).ToListAsync(ct),
                [NiveauRelance.RelanceRetardJ7] = await enCours
                    .Where(e => e.DateRetourPrevue <= aujourdhui.AddDays(-r.RelanceValidationJours)).ToListAsync(ct),
            };
            var crees = new Dictionary<NiveauRelance, int>();
            foreach (var (niveau, emprunts) in candidats)
            {
                crees[niveau] = 0;
                foreach (var e in emprunts)
                    if (await CreerAsync(e, niveau, ct) is not null) crees[niveau]++;
            }
            return crees;
        }
        finally
        {
            Verrou.Release();
        }
    }

    public async Task<Relance?> CreerAsync(Emprunt emprunt, NiveauRelance niveau, CancellationToken ct = default)
    {
        var cle = Relance.Cle(emprunt.Id, niveau, emprunt.DateRetourPrevue);
        if (await db.Relances.AnyAsync(x => x.CleIdempotence == cle, ct)) return null;

        var classement = politique.ClasserRelance(niveau);
        var action = PolitiqueActions.ActionRelance(niveau);
        var decision = journal.Ouvrir("Planificateur : détection des échéances", action);
        decision.EmpruntId = emprunt.Id;
        decision.DemandeurId = emprunt.UtilisateurId;
        decision.Motif = $"{niveau} : « {emprunt.Livre.Titre} », échéance du {emprunt.DateRetourPrevue:dd/MM/yyyy} ({classement}).";
        decision.DonneesJson = JournalDecisions.Json(new { niveau = niveau.ToString(), echeance = emprunt.DateRetourPrevue });

        var relance = new Relance
        {
            Emprunt = emprunt,
            Niveau = niveau,
            Echeance = emprunt.DateRetourPrevue,
            PrevueLe = horloge.Maintenant(),
            CleIdempotence = cle,
            Decision = decision,
            Statut = classement == Classement.ValidationRequise ? StatutRelance.EnAttenteValidation : StatutRelance.Planifiee,
        };
        db.Relances.Add(relance);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Index unique : une autre détection a créé la même relance au même instant.
            db.ChangeTracker.Clear();
            return null;
        }
        logger.LogInformation("Relance {Cle} créée ({Classement})", cle, classement);
        return relance;
    }

    /// <summary>Envoie les relances planifiées ou en échec (réessai) dont le nombre de tentatives n'est pas épuisé.</summary>
    public async Task<(int Envoyees, int EnEchec)> TraiterAsync(CancellationToken ct = default)
    {
        var ids = await db.Relances
            .Where(r => (r.Statut == StatutRelance.Planifiee || r.Statut == StatutRelance.Echec) && r.Tentatives < regles.Value.TentativesMax)
            .OrderBy(r => r.PrevueLe).Select(r => r.Id).ToListAsync(ct);
        int envoyees = 0, echecs = 0;
        foreach (var id in ids)
        {
            var statut = await EnvoyerAsync(id, ct);
            if (statut == StatutRelance.Envoyee) envoyees++;
            else if (statut is StatutRelance.Echec or StatutRelance.Escaladee) echecs++;
        }
        return (envoyees, echecs);
    }

    public async Task<StatutRelance> EnvoyerAsync(int relanceId, CancellationToken ct = default)
    {
        var relance = await db.Relances
            .Include(r => r.Emprunt).ThenInclude(e => e.Livre)
            .Include(r => r.Emprunt).ThenInclude(e => e.Utilisateur)
            .Include(r => r.Decision)
            .FirstOrDefaultAsync(r => r.Id == relanceId, ct) ?? throw new IntrouvableException("Relance introuvable.");

        // Déjà traitée : rejouer ne renvoie rien (idempotence).
        if (relance.Statut is not (StatutRelance.Planifiee or StatutRelance.Echec)) return relance.Statut;

        var emprunt = relance.Emprunt;
        var motifAnnulation = !emprunt.EstEnCours ? "Livre rendu entre la détection et l'envoi."
            : emprunt.DateRetourPrevue != relance.Echeance ? "Échéance modifiée (prolongation) depuis la détection."
            : null;
        if (motifAnnulation is not null)
        {
            await AnnulerAsync(relance, motifAnnulation, StatutDecision.Annulee, ct);
            return relance.Statut;
        }

        if (politique.ClasserRelance(relance.Niveau) == Classement.ValidationRequise && relance.Decision?.Statut != StatutDecision.Approuvee)
            return relance.Statut; // en attente du bibliothécaire

        relance.Tentatives++;
        relance.DerniereTentativeLe = horloge.Maintenant();
        var u = emprunt.Utilisateur;
        try
        {
            var message = await communication.NotifierAsync(u, relance.Niveau.Modele(),
                [u.Prenom, emprunt.Livre.Titre, emprunt.DateRetourPrevue.ToString("dd/MM/yyyy")], relance.CleIdempotence, ct);
            relance.Statut = StatutRelance.Envoyee;
            relance.MessageId = message.Id;
            if (relance.Decision is { } d)
            {
                d.MessageId = message.Id;
                journal.ChangerStatut(d, StatutDecision.Executee, $"Envoyée par {message.Canal}.", PolitiqueActions.ActionRelance(relance.Niveau));
            }
        }
        catch (EnvoiException e)
        {
            var epuise = e.Definitif || relance.Tentatives >= regles.Value.TentativesMax;
            relance.Statut = epuise ? StatutRelance.Escaladee : StatutRelance.Echec;
            if (relance.Decision is { } d)
                journal.ChangerStatut(d, epuise ? StatutDecision.Escaladee : d.Statut, $"Échec d'envoi (tentative {relance.Tentatives}) : {e.Message}");
            logger.LogWarning("Relance {Cle} : échec tentative {N} ({Erreur})", relance.CleIdempotence, relance.Tentatives, e.Message);
        }
        await db.SaveChangesAsync(ct);
        return relance.Statut;
    }

    public async Task ApprouverAsync(Relance relance, CancellationToken ct = default)
    {
        relance.Statut = StatutRelance.Planifiee;
        await db.SaveChangesAsync(ct);
        await EnvoyerAsync(relance.Id, ct);
    }

    public async Task AnnulerAsync(Relance relance, string motif, StatutDecision statutDecision, CancellationToken ct = default)
    {
        relance.Statut = StatutRelance.Annulee;
        if (relance.Decision is { } d && d.Statut is not (StatutDecision.Rejetee or StatutDecision.Executee))
            journal.ChangerStatut(d, statutDecision, motif, "aucune");
        await db.SaveChangesAsync(ct);
    }

    public async Task LivreRenduAsync(Emprunt emprunt, CancellationToken ct)
    {
        var enSuspens = await db.Relances.Include(r => r.Decision)
            .Where(r => r.EmpruntId == emprunt.Id && (r.Statut == StatutRelance.Planifiee || r.Statut == StatutRelance.Echec || r.Statut == StatutRelance.EnAttenteValidation))
            .ToListAsync(ct);
        foreach (var r in enSuspens) await AnnulerAsync(r, "Livre rendu : relance devenue inutile.", StatutDecision.Annulee, ct);
    }

    public Task<Relance?> PourDecisionAsync(int decisionId, CancellationToken ct = default) =>
        db.Relances.Include(r => r.Decision).FirstOrDefaultAsync(r => r.DecisionId == decisionId, ct);

    public async Task<List<RelanceDto>> ListerAsync(StatutRelance? statut, CancellationToken ct = default)
    {
        var q = db.Relances.Include(r => r.Emprunt).ThenInclude(e => e.Livre).Include(r => r.Emprunt).ThenInclude(e => e.Utilisateur).AsQueryable();
        if (statut is { } s) q = q.Where(r => r.Statut == s);
        var liste = await q.OrderByDescending(r => r.PrevueLe).Take(200).ToListAsync(ct);
        return liste.Select(r => r.ToDto()).ToList();
    }
}
