using MediaLib.Web.Components;
using MediaLib.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);
var urlApi = builder.Configuration["Api:Url"] ?? "http://localhost:5032";

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Authentification côté Blazor : l'état vient du JWT de l'API, pas d'un cookie.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AutorisationGereeParBlazor>();
builder.Services.AddScoped<MediaLibAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<MediaLibAuthStateProvider>());

builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(urlApi));
builder.Services.AddHttpClient<ApiClient>(c => c.BaseAddress = new Uri(urlApi));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Le jeton vit dans le navigateur : la requête HTTP initiale ne le porte pas. Les pages [Authorize] sont donc
/// laissées passer ici, et c'est AuthorizeRouteView (côté Blazor) qui applique les rôles et redirige vers la connexion.
/// </summary>
internal sealed class AutorisationGereeParBlazor : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult) => next(context);
}
