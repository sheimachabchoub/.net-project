using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MediaLib.Api.Infrastructure;

/// <summary>Complète le document OpenAPI : titre, description et schéma de sécurité Bearer (JWT).</summary>
public sealed class DocumentOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "MediaLib API",
            Version = "v1",
            Description = "API de la médiathèque MediaLib : catalogue, emprunts, réservations, agent IA, RAG, WhatsApp, relances et décisions. "
                          + "Authentifiez-vous avec POST /api/auth/connexion puis utilisez l'access token (Bearer).",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token JWT obtenu via /api/auth/connexion (valide 15 minutes).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
