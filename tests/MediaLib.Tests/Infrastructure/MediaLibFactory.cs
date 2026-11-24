using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediaLib.Api.Communication;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaLib.Tests.Infrastructure;

/// <summary>
/// API complète en mémoire : SQLite partagé en mémoire (migrations + seed), horloge simulée,
/// transport de messages simulé, planificateur désactivé, moteur IA simulé.
/// </summary>
public class MediaLibFactory : WebApplicationFactory<Program>
{
    public const string SecretWebhook = "secret-de-test-webhook";
    public const string JetonVerification = "jeton-verif-test";
    public const string CleJwt = "cle-de-test-pour-les-jwt-medialib-0123456789-abcdef";

    private readonly string _base = $"Data Source=medialib-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _connexion;

    public HorlogeReglable Horloge { get; } = new();

    /// <summary>Réglages supplémentaires (ex. limites de débit basses pour un test précis).</summary>
    public Dictionary<string, string?> Reglages { get; } = [];

    public MediaLibFactory()
    {
        // La base en mémoire vit tant qu'une connexion reste ouverte.
        _connexion = new SqliteConnection(_base);
        _connexion.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var reglages = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediaLib"] = _base,
            ["Base:InitialiserAuDemarrage"] = "true",
            ["Base:EmpruntsDemo"] = "false",
            ["Jwt:Cle"] = CleJwt,
            ["Planificateur:Actif"] = "false",
            ["LimiteDebit:AuthParMinute"] = "10000",
            ["LimiteDebit:AgentParMinute"] = "10000",
            ["Agent:Mode"] = "simule",
            ["Agent:MessagesParMinute"] = "1000",
            ["Messagerie:Transport"] = "simule",
            ["WhatsApp:SecretApplication"] = SecretWebhook,
            ["WhatsApp:JetonVerification"] = JetonVerification,
        };
        foreach (var (cle, valeur) in Reglages) reglages[cle] = valeur;
        foreach (var (cle, valeur) in reglages) builder.UseSetting(cle, valeur);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Horloge);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connexion.Dispose();
    }

    public BoiteEnvoiSimulee Boite => Services.GetRequiredService<BoiteEnvoiSimulee>();

    public async Task<T> AvecScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task AvecScopeAsync(Func<IServiceProvider, Task> action) =>
        AvecScopeAsync<bool>(async sp => { await action(sp); return true; });

    public async Task<JetonsReponse> ConnecterAsync(string email, string motDePasse)
    {
        var reponse = await CreateClient().PostAsJsonAsync("api/auth/connexion", new ConnexionRequete { Email = email, MotDePasse = motDePasse });
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<JetonsReponse>(MediaLibJson.Options))!;
    }

    public HttpClient Client(string? jeton)
    {
        var client = CreateClient();
        if (jeton is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return client;
    }

    public async Task<HttpClient> ClientAsync(string email, string motDePasse) => Client((await ConnecterAsync(email, motDePasse)).AccessToken);

    public Task<HttpClient> BibliothecaireAsync() => ClientAsync(ComptesDemo.BibliothecaireEmail, ComptesDemo.BibliothecaireMotDePasse);

    public Task<HttpClient> AdminAsync() => ClientAsync(ComptesDemo.AdminEmail, ComptesDemo.AdminMotDePasse);

    /// <summary>Inscrit un nouveau membre (e-mail unique) et renvoie son client authentifié et son identifiant.</summary>
    public async Task<(HttpClient Client, UtilisateurDto Utilisateur)> NouveauMembreAsync(string? prenom = null)
    {
        var email = $"membre-{Guid.NewGuid():N}@test.local";
        const string motDePasse = "Secret123";
        var reponse = await CreateClient().PostAsJsonAsync("api/auth/inscription", new InscriptionRequete
        {
            Email = email, Prenom = prenom ?? "Test", Nom = "Membre", MotDePasse = motDePasse, ConfirmationMotDePasse = motDePasse,
        });
        reponse.EnsureSuccessStatusCode();
        var jetons = await ConnecterAsync(email, motDePasse);
        return (Client(jetons.AccessToken), jetons.Utilisateur);
    }

    /// <summary>Crée un livre dédié au test (ISBN unique) pour éviter les interférences entre tests.</summary>
    public Task<Livre> NouveauLivreAsync(int exemplaires = 1, string? titre = null) => AvecScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<MediaLibDbContext>();
        var douze = "979" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
        var livre = new Livre
        {
            Titre = titre ?? $"Livre de test {Guid.NewGuid():N}"[..28],
            Isbn = Seed.Isbn13(douze),
            Annee = 2020,
            NbExemplaires = exemplaires,
            NbDisponibles = exemplaires,
            AuteurId = db.Auteurs.First().Id,
            CategorieId = db.Categories.First().Id,
        };
        db.Livres.Add(livre);
        await db.SaveChangesAsync();
        return livre;
    });
}

public static class HttpExtensions
{
    public static async Task<T> LireAsync<T>(this HttpResponseMessage reponse) =>
        (await reponse.Content.ReadFromJsonAsync<T>(MediaLibJson.Options))!;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object? corps = null) =>
        client.PostAsJsonAsync(url, corps ?? new { }, MediaLibJson.Options);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object corps) =>
        client.PutAsJsonAsync(url, corps, MediaLibJson.Options);
}

/// <summary>Horloge de test qu'on peut avancer puis remettre à l'heure réelle (les JWT restent valides).</summary>
public class HorlogeReglable : TimeProvider
{
    private TimeSpan _decalage;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _decalage;

    public void Advance(TimeSpan duree) => _decalage += duree;

    public void SetUtcNow(DateTimeOffset _) => _decalage = TimeSpan.Zero;
}
