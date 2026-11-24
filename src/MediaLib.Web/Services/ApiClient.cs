using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaLib.Shared;
using Microsoft.AspNetCore.Components;

namespace MediaLib.Web.Services;

/// <summary>Résultat d'un appel à l'API : valeur ou message d'erreur lisible.</summary>
public record Resultat<T>(bool Ok, T? Valeur, string? Erreur, HttpStatusCode Statut)
{
    public static Resultat<T> Echec(string erreur, HttpStatusCode statut) => new(false, default, erreur, statut);
}

/// <summary>
/// Client HTTP vers MediaLib.Api : ajoute le jeton, le rafraîchit sur 401, et renvoie vers la connexion
/// (avec la page demandée) quand la session a expiré. Blazor ne manipule que des DTO de MediaLib.Shared.
/// </summary>
public class ApiClient(HttpClient http, MediaLibAuthStateProvider auth, NavigationManager navigation)
{
    public Task<Resultat<T>> GetAsync<T>(string url) => EnvoyerAsync<T>(HttpMethod.Get, url, null);

    public Task<Resultat<T>> PostAsync<T>(string url, object? corps = null) => EnvoyerAsync<T>(HttpMethod.Post, url, corps ?? new { });

    public Task<Resultat<T>> PutAsync<T>(string url, object corps) => EnvoyerAsync<T>(HttpMethod.Put, url, corps);

    public Task<Resultat<bool>> DeleteAsync(string url) => EnvoyerAsync<bool>(HttpMethod.Delete, url, null);

    private async Task<Resultat<T>> EnvoyerAsync<T>(HttpMethod methode, string url, object? corps)
    {
        try
        {
            var jeton = await auth.JetonValideAsync();
            var reponse = await EnvoyerAvecJetonAsync(methode, url, corps, jeton);
            if (reponse.StatusCode == HttpStatusCode.Unauthorized && jeton is not null)
            {
                // Access token refusé (expiré, rôles changés…) : un seul essai de refresh.
                jeton = await auth.JetonValideAsync(forcerRafraichissement: true);
                if (jeton is not null) reponse = await EnvoyerAvecJetonAsync(methode, url, corps, jeton);
                if (reponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await auth.DeconnecterAsync(expiree: true);
                    RedirigerVersConnexion();
                    return Resultat<T>.Echec("Votre session a expiré. Reconnectez-vous.", HttpStatusCode.Unauthorized);
                }
            }
            if (!reponse.IsSuccessStatusCode) return Resultat<T>.Echec(await LireErreurAsync(reponse), reponse.StatusCode);
            if (typeof(T) == typeof(bool) || reponse.StatusCode is HttpStatusCode.NoContent || reponse.Content.Headers.ContentLength == 0)
                return new Resultat<T>(true, typeof(T) == typeof(bool) ? (T)(object)true : default, null, reponse.StatusCode);
            return new Resultat<T>(true, await reponse.Content.ReadFromJsonAsync<T>(MediaLibJson.Options), null, reponse.StatusCode);
        }
        catch (HttpRequestException e)
        {
            return Resultat<T>.Echec("L'API MediaLib est injoignable : " + e.Message, HttpStatusCode.ServiceUnavailable);
        }
    }

    private async Task<HttpResponseMessage> EnvoyerAvecJetonAsync(HttpMethod methode, string url, object? corps, string? jeton)
    {
        using var requete = new HttpRequestMessage(methode, url);
        if (corps is not null) requete.Content = JsonContent.Create(corps, corps.GetType(), options: MediaLibJson.Options);
        if (jeton is not null) requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return await http.SendAsync(requete);
    }

    private void RedirigerVersConnexion()
    {
        var relative = navigation.ToBaseRelativePath(navigation.Uri);
        navigation.NavigateTo($"connexion?expire=1&returnUrl={Uri.EscapeDataString("/" + relative)}");
    }

    /// <summary>Transforme un ProblemDetails (ou ValidationProblemDetails) en message lisible.</summary>
    public static async Task<string> LireErreurAsync(HttpResponseMessage reponse)
    {
        if (reponse.StatusCode == HttpStatusCode.TooManyRequests) return "Trop de requêtes : patientez une minute.";
        if (reponse.StatusCode == HttpStatusCode.Forbidden) return "Accès refusé : votre rôle ne permet pas cette action.";
        try
        {
            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
            var racine = doc.RootElement;
            if (racine.TryGetProperty("errors", out var erreurs) && erreurs.ValueKind == JsonValueKind.Object)
                return string.Join(" ", erreurs.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            if (racine.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String) return detail.GetString()!;
            if (racine.TryGetProperty("title", out var titre) && titre.ValueKind == JsonValueKind.String) return titre.GetString()!;
        }
        catch (JsonException)
        {
        }
        return $"Erreur {(int)reponse.StatusCode}.";
    }
}
