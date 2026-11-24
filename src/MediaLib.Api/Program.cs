using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using MediaLib.Api.Agent;
using MediaLib.Api.Auth;
using MediaLib.Api.Communication;
using MediaLib.Api.Configuration;
using MediaLib.Api.Connaissance;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Infrastructure;
using MediaLib.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var config = builder.Configuration;

// ------------------------------------------------------------------ options (validées au démarrage)
services.AddOptions<ReglesBibliotheque>().Bind(config.GetSection(ReglesBibliotheque.Section)).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<AgentOptions>().Bind(config.GetSection(AgentOptions.Section))
    .PostConfigure(o => o.CleApi ??= config["ANTHROPIC_API_KEY"]).ValidateDataAnnotations();
services.AddOptions<MessagerieOptions>().Bind(config.GetSection(MessagerieOptions.Section));
services.AddOptions<WhatsAppOptions>().Bind(config.GetSection(WhatsAppOptions.Section));
services.AddOptions<PlanificateurOptions>().Bind(config.GetSection(PlanificateurOptions.Section)).ValidateDataAnnotations();

// ------------------------------------------------------------------ données et Identity
services.AddDbContext<MediaLibDbContext>(o => o.UseSqlite(config.GetConnectionString("MediaLib") ?? "Data Source=medialib.db"));
services.AddIdentityCore<Utilisateur>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireUppercase = true;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<MediaLibDbContext>()
    .AddErrorDescriber<DescripteurErreursFr>();
services.AddSingleton(TimeProvider.System);

// ------------------------------------------------------------------ JWT
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Emetteur,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = AuthService.Cle(jwt.Value),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = Revendications.Nom,
            RoleClaimType = Revendications.Role,
        };
    });
services.AddAuthorization();

// ------------------------------------------------------------------ limitation de débit
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("LimiteDebit:AuthParMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
    o.AddPolicy("agent", http => RateLimitPartition.GetSlidingWindowLimiter(
        http.User.IdUtilisateurOuNull() ?? http.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("LimiteDebit:AgentParMinute", 20),
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
        }));
});

// ------------------------------------------------------------------ services métier
services.AddSingleton<PolitiqueActions>();
services.AddScoped<JournalDecisions>();
services.AddScoped<CatalogueService>();
services.AddScoped<EmpruntService>();
services.AddScoped<ReservationService>();
services.AddScoped<RelanceService>();
services.AddScoped<IObservateurRetour>(sp => sp.GetRequiredService<ReservationService>());
services.AddScoped<IObservateurRetour>(sp => sp.GetRequiredService<RelanceService>());
services.AddScoped<DemandesAdherent>();
services.AddScoped<ValidationDecisions>();
services.AddScoped<TraitementPlanifie>();
services.AddScoped<AuthService>();
services.AddScoped<Seed>();

// RAG
services.AddScoped<IndexeurConnaissance>();
services.AddScoped<RechercheConnaissance>();

// Communication
services.AddSingleton<BoiteEnvoiSimulee>();
services.AddSingleton<ExpediteurSimule>();
services.AddScoped<ExpediteurEmail>();
services.AddHttpClient<ExpediteurWhatsApp>(c => c.Timeout = TimeSpan.FromSeconds(15));
services.AddScoped<IExpediteur>(sp => sp.GetRequiredService<ExpediteurEmail>());
services.AddScoped<IExpediteur>(sp => sp.GetRequiredService<ExpediteurWhatsApp>());
services.AddScoped<ServiceCommunication>();
services.AddScoped<LiaisonWhatsApp>();
services.AddSingleton<AnalyseurWebhookWhatsApp>();
services.AddScoped<TraitementMessageEntrant>();

// Agent IA
services.AddSingleton<LimiteurAgent>();
services.AddScoped<ContexteDemandeur>();
services.AddScoped<JournalOutils>();
services.AddScoped<OutilsAgent>();
services.AddScoped<MoteurSimule>();
services.AddScoped<MoteurClaude>();
services.AddScoped<BibliothecaireIA>();

services.AddHostedService<PlanificateurRelances>();

// ------------------------------------------------------------------ API, erreurs, OpenAPI
services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
services.AddProblemDetails();
services.AddExceptionHandler<GestionnaireErreurs>();
services.AddOpenApi(o => o.AddDocumentTransformer<DocumentOpenApi>());

var app = builder.Build();

if (config.GetValue("Base:InitialiserAuDemarrage", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MediaLibDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seed>().ExecuterAsync(config.GetValue("Base:EmpruntsDemo", true));
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("MediaLib API").AddPreferredSecuritySchemes("Bearer"));
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapControllers();

app.Run();

/// <summary>Point d'entrée exposé aux tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
