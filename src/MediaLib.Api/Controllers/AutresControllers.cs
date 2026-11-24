using MediaLib.Api.Auth;
using MediaLib.Api.Communication;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Controllers;

/// <summary>Profil de l'utilisateur connecté et liaison WhatsApp.</summary>
[ApiController]
[Route("api/compte")]
[Authorize]
[Produces("application/json")]
public class CompteController(UserManager<Utilisateur> users, MediaLibDbContext db, LiaisonWhatsApp liaison) : ControllerBase
{
    /// <summary>Met à jour prénom, nom, numéro et canal préféré. Changer de numéro annule sa vérification.</summary>
    [HttpPut("profil")]
    [ProducesResponseType<UtilisateurDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UtilisateurDto> Profil(ProfilRequete r, CancellationToken ct)
    {
        var u = await Moi();
        var telephone = string.IsNullOrWhiteSpace(r.Telephone) ? null : r.Telephone.Trim();
        if (telephone != u.Telephone)
        {
            if (telephone is not null && await db.Users.AnyAsync(x => x.Telephone == telephone && x.Id != u.Id, ct))
                throw new RegleMetierException("Ce numéro est déjà lié à un autre compte.");
            u.Telephone = telephone;
            u.TelephoneVerifieLe = null;
        }
        u.Prenom = r.Prenom.Trim();
        u.Nom = r.Nom.Trim();
        if (r.CanalPrefere == Canal.WhatsApp && !u.PeutRecevoirWhatsapp)
            throw new RegleMetierException("WhatsApp exige un numéro valide, lié (code de liaison) et votre consentement.");
        u.CanalPrefere = r.CanalPrefere is Canal.WhatsApp ? Canal.WhatsApp : Canal.Email;
        await db.SaveChangesAsync(ct);
        return u.ToDto(await users.GetRolesAsync(u));
    }

    /// <summary>Donne ou retire le consentement aux messages WhatsApp.</summary>
    [HttpPut("whatsapp/consentement")]
    [ProducesResponseType<UtilisateurDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<UtilisateurDto> Consentement(ConsentementRequete r, CancellationToken ct)
    {
        var u = await Moi();
        await liaison.DefinirConsentementAsync(u, r.Consentement, ct);
        return u.ToDto(await users.GetRolesAsync(u));
    }

    /// <summary>Génère un code de liaison (15 min) et le lien wa.me à ouvrir depuis le téléphone.</summary>
    [HttpPost("whatsapp/liaison")]
    [ProducesResponseType<LiaisonWhatsappDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<LiaisonWhatsappDto> Liaison(CancellationToken ct) => await liaison.DemarrerAsync(await Moi(), ct);

    /// <summary>Délie le numéro WhatsApp.</summary>
    [HttpDelete("whatsapp/liaison")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delier(CancellationToken ct)
    {
        await liaison.DelierAsync(await Moi(), ct);
        return NoContent();
    }

    private async Task<Utilisateur> Moi() =>
        await users.FindByIdAsync(User.IdUtilisateur()) ?? throw new IntrouvableException("Compte introuvable.");
}

/// <summary>Administration des utilisateurs et des rôles.</summary>
[ApiController]
[Route("api/admin/utilisateurs")]
[Authorize(Roles = Roles.Admin)]
[Produces("application/json")]
public class AdminController(UserManager<Utilisateur> users, MediaLibDbContext db, AuthService auth, TimeProvider horloge) : ControllerBase
{
    /// <summary>Liste des utilisateurs avec rôles, verrouillage et emprunts en cours.</summary>
    [HttpGet]
    [ProducesResponseType<List<UtilisateurAdminDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<UtilisateurAdminDto>> Lister([FromQuery] string? q, CancellationToken ct)
    {
        var requete = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) requete = requete.Where(u => u.Email!.Contains(q) || u.Nom.Contains(q) || u.Prenom.Contains(q));
        var liste = await requete.OrderBy(u => u.Nom).Take(200).ToListAsync(ct);
        var resultat = new List<UtilisateurAdminDto>();
        foreach (var u in liste)
        {
            var enCours = await db.Emprunts.CountAsync(e => e.UtilisateurId == u.Id && e.DateRetourReelle == null, ct);
            var verrouille = u.LockoutEnd is { } fin && fin > horloge.GetUtcNow();
            resultat.Add(new UtilisateurAdminDto(u.Id, u.Email ?? "", u.NomComplet, (await users.GetRolesAsync(u)).ToList(), verrouille, u.LockoutEnd, enCours));
        }
        return resultat;
    }

    /// <summary>Remplace les rôles d'un utilisateur (Membre, Bibliothecaire, Admin).</summary>
    [HttpPut("{id}/roles")]
    [ProducesResponseType<UtilisateurAdminDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UtilisateurAdminDto> ModifierRoles(string id, RolesRequete r, CancellationToken ct)
    {
        var inconnus = r.Roles.Except(Roles.Tous).ToList();
        if (inconnus.Count > 0) throw new ArgumentException("Rôle(s) inconnu(s) : " + string.Join(", ", inconnus));
        var u = await users.FindByIdAsync(id) ?? throw new IntrouvableException("Utilisateur introuvable.");
        if (u.Id == User.IdUtilisateur() && !r.Roles.Contains(Roles.Admin))
            throw new RegleMetierException("Vous ne pouvez pas retirer votre propre rôle Admin.");
        var actuels = await users.GetRolesAsync(u);
        await users.RemoveFromRolesAsync(u, actuels.Except(r.Roles));
        await users.AddToRolesAsync(u, r.Roles.Except(actuels));
        // Les jetons en circulation portent les anciens rôles : on force une reconnexion.
        await auth.RevoquerToutAsync(u.Id, ct);
        var enCours = await db.Emprunts.CountAsync(e => e.UtilisateurId == u.Id && e.DateRetourReelle == null, ct);
        return new UtilisateurAdminDto(u.Id, u.Email ?? "", u.NomComplet, (await users.GetRolesAsync(u)).ToList(), u.LockoutEnd > horloge.GetUtcNow(), u.LockoutEnd, enCours);
    }

    /// <summary>Déverrouille un compte bloqué après trop d'échecs de connexion.</summary>
    [HttpPost("{id}/deverrouiller")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deverrouiller(string id)
    {
        var u = await users.FindByIdAsync(id) ?? throw new IntrouvableException("Utilisateur introuvable.");
        await users.SetLockoutEndDateAsync(u, null);
        await users.ResetAccessFailedCountAsync(u);
        return NoContent();
    }
}

/// <summary>Relances : liste et déclenchement manuel du traitement planifié.</summary>
[ApiController]
[Route("api/relances")]
[Authorize(Roles = Roles.Gestion)]
[Produces("application/json")]
public class RelancesController(RelanceService relances, TraitementPlanifie traitement) : ControllerBase
{
    /// <summary>Relances (filtre facultatif par statut).</summary>
    [HttpGet]
    [ProducesResponseType<List<RelanceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<RelanceDto>> Lister([FromQuery] StatutRelance? statut, CancellationToken ct) => await relances.ListerAsync(statut, ct);

    /// <summary>Lance tout de suite la détection, l'envoi (avec réessai) et l'expiration des réservations.</summary>
    [HttpPost("traitement")]
    [ProducesResponseType<TraitementRelancesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<TraitementRelancesDto> Traiter(CancellationToken ct) => await traitement.ExecuterAsync(ct);
}

/// <summary>Webhook WhatsApp Business (Meta) : vérification d'abonnement, messages entrants et statuts.</summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
public class WebhookWhatsAppController(AnalyseurWebhookWhatsApp analyseur, TraitementMessageEntrant traitement, ILogger<WebhookWhatsAppController> logger) : ControllerBase
{
    /// <summary>Vérification de l'abonnement par Meta (hub.mode, hub.verify_token, hub.challenge).</summary>
    [HttpGet]
    [Produces("text/plain")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult Verifier([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? jeton, [FromQuery(Name = "hub.challenge")] string? challenge) =>
        analyseur.AbonnementValide(mode, jeton) ? Content(challenge ?? string.Empty, "text/plain") : StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>Réception des événements ; la signature X-Hub-Signature-256 est obligatoire (sinon 403).</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Recevoir(CancellationToken ct)
    {
        Request.EnableBuffering();
        string corps;
        using (var lecteur = new StreamReader(Request.Body, leaveOpen: true)) corps = await lecteur.ReadToEndAsync(ct);
        if (!analyseur.SignatureValide(corps, Request.Headers["X-Hub-Signature-256"]))
        {
            logger.LogWarning("Webhook WhatsApp rejeté : signature absente ou invalide.");
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        List<EvenementMessage> messages;
        List<EvenementStatut> statuts;
        try
        {
            (messages, statuts) = analyseur.Analyser(corps);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest();
        }
        foreach (var m in messages) await traitement.MessageRecuAsync(m, ct);
        foreach (var s in statuts) await traitement.StatutRecuAsync(s, ct);
        return Ok();
    }
}
