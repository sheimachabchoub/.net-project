using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MediaLib.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MediaLib.Web.Services;

/// <summary>Session stockée (chiffrée par ASP.NET Core Data Protection) dans le navigateur.</summary>
public record SessionStockee(string AccessToken, DateTime ExpireLe, string RefreshToken, DateTime RefreshExpireLe, UtilisateurDto Utilisateur);

/// <summary>
/// AuthenticationStateProvider personnalisé : lit le JWT de l'API, construit l'utilisateur (nom, rôles),
/// rafraîchit l'access token avant son expiration et déconnecte proprement si le refresh token n'est plus valide.
/// </summary>
public sealed class MediaLibAuthStateProvider(
    ProtectedLocalStorage stockage,
    IHttpClientFactory httpFactory,
    ILogger<MediaLibAuthStateProvider> logger) : AuthenticationStateProvider, IDisposable
{
    private const string Cle = "medialib.session";
    private static readonly AuthenticationState Anonyme = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private SessionStockee? _session;
    private bool _charge;
    private Timer? _minuterie;

    /// <summary>Vrai si la dernière déconnexion vient d'une session expirée (message affiché sur la page de connexion).</summary>
    public bool SessionExpiree { get; private set; }

    public UtilisateurDto? Utilisateur => _session?.Utilisateur;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var session = await SessionAsync();
        return session is null ? Anonyme : new AuthenticationState(Principal(session.AccessToken));
    }

    public async Task<string?> ConnecterAsync(ConnexionRequete requete)
    {
        var http = httpFactory.CreateClient("api");
        var reponse = await http.PostAsJsonAsync("api/auth/connexion", requete, MediaLibJson.Options);
        if (!reponse.IsSuccessStatusCode) return await ApiClient.LireErreurAsync(reponse);
        var jetons = await reponse.Content.ReadFromJsonAsync<JetonsReponse>(MediaLibJson.Options);
        await OuvrirAsync(jetons!);
        return null;
    }

    public async Task DeconnecterAsync(bool expiree = false)
    {
        var session = _session;
        if (session is not null && !expiree)
        {
            try
            {
                var http = httpFactory.CreateClient("api");
                using var requete = new HttpRequestMessage(HttpMethod.Post, "api/auth/deconnexion")
                {
                    Content = JsonContent.Create(new RefreshRequete { RefreshToken = session.RefreshToken }, options: MediaLibJson.Options),
                };
                requete.Headers.Authorization = new("Bearer", session.AccessToken);
                await http.SendAsync(requete);
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("Révocation du refresh token impossible : {Erreur}", e.Message);
            }
        }
        await EffacerAsync(expiree);
    }

    /// <summary>Renvoie un access token valide (rafraîchi si besoin), ou null si la session est terminée.</summary>
    public async Task<string?> JetonValideAsync(bool forcerRafraichissement = false)
    {
        var session = await SessionAsync();
        if (session is null) return null;
        if (!forcerRafraichissement && session.ExpireLe > DateTime.UtcNow.AddSeconds(30)) return session.AccessToken;
        return await RafraichirAsync(session) ? _session?.AccessToken : null;
    }

    public void Dispose()
    {
        _minuterie?.Dispose();
        _verrou.Dispose();
    }

    private async Task<SessionStockee?> SessionAsync()
    {
        if (_charge) return _session;
        await _verrou.WaitAsync();
        try
        {
            if (_charge) return _session;
            try
            {
                var lu = await stockage.GetAsync<SessionStockee>(Cle);
                _session = lu.Success ? lu.Value : null;
            }
            catch (Exception e) when (e is InvalidOperationException or System.Security.Cryptography.CryptographicException or JsonException)
            {
                // Pas encore de JS (pré-rendu) ou donnée illisible : session vide.
                logger.LogDebug("Session illisible : {Erreur}", e.Message);
                _session = null;
            }
            _charge = true;
            if (_session is not null && _session.RefreshExpireLe <= DateTime.UtcNow) _session = null;
            Planifier();
            return _session;
        }
        finally
        {
            _verrou.Release();
        }
    }

    private async Task<bool> RafraichirAsync(SessionStockee session)
    {
        await _verrou.WaitAsync();
        try
        {
            // Un autre appel vient peut-être de rafraîchir.
            if (_session is not null && _session.AccessToken != session.AccessToken && _session.ExpireLe > DateTime.UtcNow.AddSeconds(30)) return true;
            var http = httpFactory.CreateClient("api");
            var reponse = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = session.RefreshToken }, MediaLibJson.Options);
            if (!reponse.IsSuccessStatusCode)
            {
                logger.LogInformation("Refresh refusé ({Statut}) : fin de session.", (int)reponse.StatusCode);
                _session = null;
            }
            else
            {
                var j = (await reponse.Content.ReadFromJsonAsync<JetonsReponse>(MediaLibJson.Options))!;
                _session = new SessionStockee(j.AccessToken, j.ExpireLe, j.RefreshToken, j.RefreshExpireLe, j.Utilisateur);
            }
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning("API injoignable pendant le refresh : {Erreur}", e.Message);
            return false;
        }
        finally
        {
            _verrou.Release();
        }

        if (_session is null)
        {
            await EffacerAsync(expiree: true);
            return false;
        }
        await stockage.SetAsync(Cle, _session);
        Planifier();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return true;
    }

    private async Task OuvrirAsync(JetonsReponse j)
    {
        _session = new SessionStockee(j.AccessToken, j.ExpireLe, j.RefreshToken, j.RefreshExpireLe, j.Utilisateur);
        _charge = true;
        SessionExpiree = false;
        await stockage.SetAsync(Cle, _session);
        Planifier();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task EffacerAsync(bool expiree)
    {
        _session = null;
        _charge = true;
        SessionExpiree = expiree;
        _minuterie?.Dispose();
        _minuterie = null;
        try
        {
            await stockage.DeleteAsync(Cle);
        }
        catch (InvalidOperationException)
        {
        }
        NotifyAuthenticationStateChanged(Task.FromResult(Anonyme));
    }

    /// <summary>Rafraîchit automatiquement 30 secondes avant l'expiration de l'access token.</summary>
    private void Planifier()
    {
        _minuterie?.Dispose();
        _minuterie = null;
        if (_session is not { } s) return;
        var delai = s.ExpireLe - DateTime.UtcNow - TimeSpan.FromSeconds(30);
        if (delai < TimeSpan.Zero) delai = TimeSpan.Zero;
        _minuterie = new Timer(_ => _ = RafraichirAsync(s), null, delai, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Construit le ClaimsPrincipal à partir de la charge utile du JWT (sub, email, name, role).</summary>
    public static ClaimsPrincipal Principal(string jwt)
    {
        var parties = jwt.Split('.');
        if (parties.Length < 2) return new ClaimsPrincipal(new ClaimsIdentity());
        var base64 = parties[1].Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        var claims = new List<Claim>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array)
                claims.AddRange(p.Value.EnumerateArray().Select(v => new Claim(p.Name, v.ToString())));
            else
                claims.Add(new Claim(p.Name, p.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", "name", "role"));
    }
}
