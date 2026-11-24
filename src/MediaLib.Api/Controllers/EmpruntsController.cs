using MediaLib.Api.Auth;
using MediaLib.Api.Configuration;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Controllers;

/// <summary>Emprunts : emprunter, mes emprunts, historique, retours, retards, prolongation.</summary>
[ApiController]
[Route("api/emprunts")]
[Authorize]
[Produces("application/json")]
public class EmpruntsController(EmpruntService emprunts, DemandesAdherent demandes) : ControllerBase
{
    /// <summary>
    /// Emprunte un livre pour soi (Membre). Un bibliothécaire peut indiquer <c>utilisateurId</c> pour enregistrer un prêt au comptoir.
    /// Max 5 emprunts en cours, durée 21 jours, exemplaire disponible obligatoire (sinon 409).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Membre + "," + Roles.Gestion)]
    [ProducesResponseType<EmpruntDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmpruntDto>> Emprunter(EmpruntRequete requete, CancellationToken ct)
    {
        // L'identité vient du jeton ; seul un bibliothécaire peut agir pour un autre adhérent.
        var pour = User.EstGestionnaire() && !string.IsNullOrWhiteSpace(requete.UtilisateurId) ? requete.UtilisateurId : User.IdUtilisateur();
        var emprunt = await emprunts.EmprunterAsync(requete.LivreId, pour, ct);
        return CreatedAtAction(nameof(Detail), new { id = emprunt.Id }, emprunts.Dto(emprunt));
    }

    /// <summary>Détail d'un emprunt (le sien, ou n'importe lequel pour un bibliothécaire).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<EmpruntDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<EmpruntDto> Detail(int id, CancellationToken ct)
    {
        var e = await emprunts.Requete().FirstOrDefaultAsync(x => x.Id == id, ct);
        // Un membre ne voit pas qu'un emprunt d'autrui existe : 404 plutôt que 403.
        if (e is null || (!User.EstGestionnaire() && e.UtilisateurId != User.IdUtilisateur())) throw new IntrouvableException("Emprunt introuvable.");
        return emprunts.Dto(e);
    }

    /// <summary>Mes emprunts en cours (ou mon historique avec <c>historique=true</c>).</summary>
    [HttpGet("moi")]
    [ProducesResponseType<List<EmpruntDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<EmpruntDto>> Mes([FromQuery] bool historique = false, CancellationToken ct = default) =>
        (await emprunts.MesEmpruntsAsync(User.IdUtilisateur(), historique, ct)).Select(emprunts.Dto).ToList();

    /// <summary>Mon historique (emprunts rendus).</summary>
    [HttpGet("moi/historique")]
    [ProducesResponseType<List<EmpruntDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<List<EmpruntDto>> Historique(CancellationToken ct) => Mes(true, ct);

    /// <summary>Suivi des emprunts (bibliothécaire) : en cours, ou seulement les retards avec <c>enRetard=true</c>.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<List<EmpruntDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<EmpruntDto>> Suivi([FromQuery] bool enRetard = false, [FromQuery] string? utilisateurId = null, CancellationToken ct = default)
    {
        if (enRetard) return (await emprunts.EnRetardAsync(ct)).Select(emprunts.Dto).ToList();
        var q = emprunts.Requete().Where(e => e.DateRetourReelle == null);
        if (!string.IsNullOrEmpty(utilisateurId)) q = q.Where(e => e.UtilisateurId == utilisateurId);
        return (await q.OrderBy(e => e.DateRetourPrevue).ToListAsync(ct)).Select(emprunts.Dto).ToList();
    }

    /// <summary>Enregistre le retour d'un livre (bibliothécaire). Prévient le premier de la file de réservation.</summary>
    [HttpPost("{id:int}/retour")]
    [Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<EmpruntDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<EmpruntDto> Retour(int id, CancellationToken ct) => emprunts.Dto(await emprunts.EnregistrerRetourAsync(id, ct));

    /// <summary>Demande de prolongation d'un de ses emprunts : crée une décision à valider par un bibliothécaire (202).</summary>
    [HttpPost("{id:int}/prolongation")]
    [Authorize(Roles = Roles.Membre)]
    [ProducesResponseType<DecisionDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DecisionDto>> Prolongation(int id, CancellationToken ct)
    {
        var d = await demandes.DemanderProlongationAsync(id, User.IdUtilisateur(), "Espace adhérent (Blazor)", null, ct);
        return Accepted(d.ToDto());
    }
}

/// <summary>Réservations d'un livre indisponible.</summary>
[ApiController]
[Route("api/reservations")]
[Authorize(Roles = Roles.Membre)]
[Produces("application/json")]
public class ReservationsController(ReservationService reservations) : ControllerBase
{
    /// <summary>Réserve un livre indisponible (file d'attente).</summary>
    [HttpPost]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> Reserver(ReservationRequete requete, CancellationToken ct)
    {
        var r = await reservations.ReserverAsync(requete.LivreId, User.IdUtilisateur(), ct);
        var dto = new ReservationDto(r.Id, r.LivreId, r.Livre.Titre, r.Statut, r.CreeLe, r.ExpireLe, await reservations.PositionAsync(r, ct));
        return Created($"/api/reservations/moi#{r.Id}", dto);
    }

    /// <summary>Mes réservations.</summary>
    [HttpGet("moi")]
    [ProducesResponseType<List<ReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<ReservationDto>> Mes(CancellationToken ct) => await reservations.MesReservationsAsync(User.IdUtilisateur(), ct);

    /// <summary>Annule une de mes réservations.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Annuler(int id, CancellationToken ct)
    {
        await reservations.AnnulerAsync(id, User.IdUtilisateur(), ct);
        return NoContent();
    }
}

/// <summary>Règles de prêt en vigueur (publiques).</summary>
[ApiController]
[Route("api/regles")]
[AllowAnonymous]
public class ReglesController(IOptions<ReglesBibliotheque> regles, PolitiqueActions politique) : ControllerBase
{
    /// <summary>Règles configurables de la bibliothèque.</summary>
    [HttpGet]
    [ProducesResponseType<ReglesDto>(StatusCodes.Status200OK)]
    public ReglesDto Lire()
    {
        var r = regles.Value;
        return new ReglesDto(r.MaxEmprunts, r.DureeJours, r.JoursAvantEcheance, r.ProlongationJours, r.ProlongationsMax, r.DelaiRetraitReservationJours, r.TentativesMax);
    }

    /// <summary>Politique des actions de l'agent (automatique / validation requise / interdite).</summary>
    [HttpGet("politique")]
    [ProducesResponseType<Dictionary<string, Classement>>(StatusCodes.Status200OK)]
    public IReadOnlyDictionary<string, Classement> Politique() => politique.ToutesLesRegles();
}
