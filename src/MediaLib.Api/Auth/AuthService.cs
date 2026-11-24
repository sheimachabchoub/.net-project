using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MediaLib.Api.Auth;

public static class Revendications
{
    public const string Id = JwtRegisteredClaimNames.Sub;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Nom = "name";
    public const string Role = "role";

    public static string IdUtilisateur(this ClaimsPrincipal p) =>
        p.FindFirstValue(Id) ?? throw new AccesRefuseException("Utilisateur non identifié.");

    public static string? IdUtilisateurOuNull(this ClaimsPrincipal p) => p.FindFirstValue(Id);

    public static bool EstGestionnaire(this ClaimsPrincipal p) => p.IsInRole(Roles.Bibliothecaire) || p.IsInRole(Roles.Admin);
}

/// <summary>Échec d'authentification : traduit en 401.</summary>
public class AuthentificationException(string message) : Exception(message);

/// <summary>Inscription, connexion (verrouillage après 5 échecs), refresh token tournant et révocable, déconnexion.</summary>
public class AuthService(
    UserManager<Utilisateur> users,
    MediaLibDbContext db,
    IOptions<JwtOptions> jwt,
    TimeProvider horloge,
    ILogger<AuthService> logger)
{
    public static SymmetricSecurityKey Cle(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Cle));

    public async Task<UtilisateurDto> InscrireAsync(InscriptionRequete r, CancellationToken ct = default)
    {
        if (await users.FindByEmailAsync(r.Email) is not null) throw new RegleMetierException("Un compte existe déjà avec cet e-mail.");
        var u = new Utilisateur
        {
            UserName = r.Email, Email = r.Email, Prenom = r.Prenom.Trim(), Nom = r.Nom.Trim(),
            InscritLe = horloge.Maintenant(), LockoutEnabled = true,
        };
        var resultat = await users.CreateAsync(u, r.MotDePasse);
        if (!resultat.Succeeded) throw new ValidationIdentityException(resultat.Errors);
        await users.AddToRoleAsync(u, Roles.Membre);
        return u.ToDto([Roles.Membre]);
    }

    public async Task<JetonsReponse> ConnecterAsync(ConnexionRequete r, CancellationToken ct = default)
    {
        var u = await users.FindByEmailAsync(r.Email);
        if (u is null) throw new AuthentificationException("E-mail ou mot de passe incorrect.");
        if (await users.IsLockedOutAsync(u))
            throw new AuthentificationException("Compte verrouillé après trop d'échecs. Réessayez plus tard.");
        if (!await users.CheckPasswordAsync(u, r.MotDePasse))
        {
            await users.AccessFailedAsync(u);
            logger.LogWarning("Échec de connexion pour {Email}", r.Email);
            throw new AuthentificationException(await users.IsLockedOutAsync(u)
                ? "Compte verrouillé après 5 échecs. Réessayez dans 15 minutes."
                : "E-mail ou mot de passe incorrect.");
        }
        await users.ResetAccessFailedCountAsync(u);
        return await EmettreAsync(u, ct);
    }

    public async Task<JetonsReponse> RafraichirAsync(string refreshToken, CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        var hash = Hacher(refreshToken);
        var existant = await db.RefreshTokens.Include(t => t.Utilisateur).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existant is null) throw new AuthentificationException("Refresh token inconnu.");
        if (existant.RevoqueLe is not null)
        {
            // Réutilisation d'un jeton déjà remplacé : vol probable, toute la session est révoquée.
            if (existant.RemplaceParHash is not null) await RevoquerToutAsync(existant.UtilisateurId, ct);
            throw new AuthentificationException("Refresh token révoqué.");
        }
        if (existant.ExpireLe <= maintenant) throw new AuthentificationException("Refresh token expiré.");
        if (await users.IsLockedOutAsync(existant.Utilisateur)) throw new AuthentificationException("Compte verrouillé.");

        var reponse = await EmettreAsync(existant.Utilisateur, ct, existant);
        return reponse;
    }

    public async Task DeconnecterAsync(string refreshToken, string utilisateurId, CancellationToken ct = default)
    {
        var hash = Hacher(refreshToken);
        var jeton = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UtilisateurId == utilisateurId, ct);
        if (jeton is { RevoqueLe: null })
        {
            jeton.RevoqueLe = horloge.Maintenant();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RevoquerToutAsync(string utilisateurId, CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        await db.RefreshTokens.Where(t => t.UtilisateurId == utilisateurId && t.RevoqueLe == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevoqueLe, maintenant), ct);
    }

    private async Task<JetonsReponse> EmettreAsync(Utilisateur u, CancellationToken ct, RefreshToken? remplace = null)
    {
        var o = jwt.Value;
        var maintenant = horloge.Maintenant();
        var roles = await users.GetRolesAsync(u);
        var claims = new List<Claim>
        {
            new(Revendications.Id, u.Id),
            new(Revendications.Email, u.Email ?? string.Empty),
            new(Revendications.Nom, u.NomComplet),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim(Revendications.Role, r)));
        var expire = maintenant.AddMinutes(o.DureeAccesMinutes);
        var jeton = new JwtSecurityToken(o.Emetteur, o.Audience, claims, notBefore: maintenant, expires: expire,
            signingCredentials: new SigningCredentials(Cle(o), SecurityAlgorithms.HmacSha256));
        var acces = new JwtSecurityTokenHandler().WriteToken(jeton);

        var brut = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var refresh = new RefreshToken
        {
            UtilisateurId = u.Id, TokenHash = Hacher(brut), CreeLe = maintenant, ExpireLe = maintenant.AddDays(o.DureeRefreshJours),
        };
        db.RefreshTokens.Add(refresh);
        if (remplace is not null)
        {
            remplace.RevoqueLe = maintenant;
            remplace.RemplaceParHash = refresh.TokenHash;
        }
        await db.SaveChangesAsync(ct);
        return new JetonsReponse(acces, expire, brut, refresh.ExpireLe, u.ToDto(roles));
    }

    public static string Hacher(string valeur) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valeur)));
}

/// <summary>Erreurs de validation Identity (mot de passe trop faible…) : traduites en 400.</summary>
public class ValidationIdentityException(IEnumerable<IdentityError> erreurs)
    : Exception(string.Join(" ", erreurs.Select(e => e.Description)))
{
    public IReadOnlyList<IdentityError> Erreurs { get; } = erreurs.ToList();
}
