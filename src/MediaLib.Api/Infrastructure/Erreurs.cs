using MediaLib.Api.Auth;
using MediaLib.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MediaLib.Api.Infrastructure;

/// <summary>Traduit les exceptions métier en réponses ProblemDetails (400, 401, 403, 404, 409).</summary>
public class GestionnaireErreurs(IProblemDetailsService problemes, ILogger<GestionnaireErreurs> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (statut, titre) = exception switch
        {
            RegleMetierException => (StatusCodes.Status409Conflict, "Règle métier non respectée"),
            IntrouvableException => (StatusCodes.Status404NotFound, "Ressource introuvable"),
            AccesRefuseException => (StatusCodes.Status403Forbidden, "Accès refusé"),
            AuthentificationException => (StatusCodes.Status401Unauthorized, "Authentification refusée"),
            ValidationIdentityException or ArgumentException => (StatusCodes.Status400BadRequest, "Requête invalide"),
            _ => (StatusCodes.Status500InternalServerError, "Erreur interne"),
        };
        if (statut == 500) logger.LogError(exception, "Erreur non gérée");
        http.Response.StatusCode = statut;
        var details = new ProblemDetails
        {
            Status = statut,
            Title = titre,
            Detail = statut == 500 ? "Une erreur inattendue s'est produite." : exception.Message,
        };
        if (exception is ValidationIdentityException v)
            details.Extensions["errors"] = new Dictionary<string, string[]> { ["MotDePasse"] = v.Erreurs.Select(e => e.Description).ToArray() };
        return await problemes.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}

/// <summary>Messages d'erreur Identity en français.</summary>
public class DescripteurErreursFr : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"Le mot de passe doit contenir au moins {length} caractères." };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "Le mot de passe doit contenir au moins une majuscule." };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "Le mot de passe doit contenir au moins un chiffre." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "Un compte existe déjà avec cet e-mail." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Un compte existe déjà avec cet e-mail." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "E-mail invalide." };
}
