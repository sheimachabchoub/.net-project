using MediaLib.Api.Auth;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaLib.Api.Controllers;

/// <summary>Inscription, connexion JWT, refresh token et déconnexion.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(AuthService auth, UserManager<Utilisateur> users) : ControllerBase
{
    /// <summary>Crée un compte Membre (mot de passe ≥ 8 caractères, une majuscule, un chiffre).</summary>
    [HttpPost("inscription")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<UtilisateurDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UtilisateurDto>> Inscription(InscriptionRequete requete, CancellationToken ct)
    {
        var u = await auth.InscrireAsync(requete, ct);
        return CreatedAtAction(nameof(Moi), null, u);
    }

    /// <summary>Connexion : renvoie un access token JWT court et un refresh token. Verrouillage après 5 échecs.</summary>
    [HttpPost("connexion")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<JetonsReponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<JetonsReponse>> Connexion(ConnexionRequete requete, CancellationToken ct) =>
        Ok(await auth.ConnecterAsync(requete, ct));

    /// <summary>Échange un refresh token valide contre une nouvelle paire de jetons (l'ancien est révoqué).</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<JetonsReponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<JetonsReponse>> Refresh(RefreshRequete requete, CancellationToken ct) =>
        Ok(await auth.RafraichirAsync(requete.RefreshToken, ct));

    /// <summary>Déconnexion : révoque le refresh token fourni.</summary>
    [HttpPost("deconnexion")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Deconnexion(RefreshRequete requete, CancellationToken ct)
    {
        await auth.DeconnecterAsync(requete.RefreshToken, User.IdUtilisateur(), ct);
        return NoContent();
    }

    /// <summary>Profil de l'utilisateur connecté (identité issue du jeton).</summary>
    [HttpGet("moi")]
    [Authorize]
    [ProducesResponseType<UtilisateurDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UtilisateurDto>> Moi()
    {
        var u = await users.FindByIdAsync(User.IdUtilisateur()) ?? throw new IntrouvableException("Compte introuvable.");
        return u.ToDto(await users.GetRolesAsync(u));
    }
}
