using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaLib.Api.Configuration;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Communication;

/// <summary>Modèles de messages (nom technique = nom du modèle déclaré chez Meta).</summary>
public enum ModeleMessage { RappelEcheance, RelanceRetardJ1, RelanceRetardJ7, LiaisonConfirmee, ReservationDisponible }

public static class ModeleMessageExtensions
{
    public static string NomTechnique(this ModeleMessage m) => m switch
    {
        ModeleMessage.RappelEcheance => "rappel_echeance",
        ModeleMessage.RelanceRetardJ1 => "relance_retard_j1",
        ModeleMessage.RelanceRetardJ7 => "relance_retard_j7",
        ModeleMessage.LiaisonConfirmee => "liaison_confirmee",
        _ => "reservation_disponible",
    };

    public static string Texte(this ModeleMessage m, IReadOnlyList<string> p)
    {
        string P(int i) => i < p.Count ? p[i] : string.Empty;
        return m switch
        {
            ModeleMessage.RappelEcheance => $"Bonjour {P(0)}, le livre « {P(1)} » est à rendre le {P(2)}. Merci de le rapporter à temps.",
            ModeleMessage.RelanceRetardJ1 => $"Bonjour {P(0)}, le livre « {P(1)} » devait être rendu le {P(2)}. Merci de le rapporter dès que possible.",
            ModeleMessage.RelanceRetardJ7 => $"Bonjour {P(0)}, le livre « {P(1)} » a plus d'une semaine de retard (échéance du {P(2)}). Merci de le rapporter rapidement ou de contacter la bibliothèque.",
            ModeleMessage.LiaisonConfirmee => $"Bonjour {P(0)}, votre numéro WhatsApp est maintenant lié à votre compte MediaLib. Répondez STOP pour ne plus recevoir de messages.",
            _ => $"Bonjour {P(0)}, le livre « {P(1)} » que vous avez réservé est disponible. Il vous est réservé jusqu'au {P(2)}.",
        };
    }

    public static ModeleMessage Modele(this NiveauRelance n) => n switch
    {
        NiveauRelance.RappelAvantEcheance => ModeleMessage.RappelEcheance,
        NiveauRelance.RelanceRetardJ1 => ModeleMessage.RelanceRetardJ1,
        _ => ModeleMessage.RelanceRetardJ7,
    };
}

public record Destinataire(string? Email, string? Telephone, string? Nom);

public record ResultatEnvoi(string? IdentifiantExterne);

public interface IExpediteur
{
    Canal Canal { get; }

    Task<ResultatEnvoi> EnvoyerAsync(Message message, Destinataire destinataire, ModeleMessage? modele, IReadOnlyList<string> parametres, CancellationToken ct);
}

/// <summary>Boîte d'envoi du transport simulé (hors ligne) : consultable, et capable de simuler des pannes.</summary>
public class BoiteEnvoiSimulee
{
    public ConcurrentQueue<(Canal Canal, string Destinataire, string Contenu)> Envoyes { get; } = new();

    private int _echecsRestants;

    /// <summary>Les <paramref name="nombre"/> prochains envois échoueront (tests de réessai / repli).</summary>
    public void SimulerEchecs(int nombre, bool definitif = false, Canal? canal = null)
    {
        _echecsRestants = nombre;
        EchecDefinitif = definitif;
        CanalEnPanne = canal;
    }

    public bool EchecDefinitif { get; private set; }
    public Canal? CanalEnPanne { get; private set; }

    public bool ConsommerEchec(Canal canal)
    {
        if (CanalEnPanne is { } c && c != canal) return false;
        while (true)
        {
            var restants = Volatile.Read(ref _echecsRestants);
            if (restants <= 0) return false;
            if (Interlocked.CompareExchange(ref _echecsRestants, restants - 1, restants) == restants) return true;
        }
    }

    public void Vider()
    {
        Envoyes.Clear();
        _echecsRestants = 0;
        CanalEnPanne = null;
    }
}

/// <summary>Transport simulé : aucun envoi réel, tout est journalisé (mode hors ligne / démo / tests).</summary>
public class ExpediteurSimule(BoiteEnvoiSimulee boite, ILogger<ExpediteurSimule> logger)
{
    public Task<ResultatEnvoi> EnvoyerAsync(Canal canal, Message message, Destinataire d, CancellationToken ct)
    {
        var adresse = canal == Canal.WhatsApp ? d.Telephone : d.Email;
        if (string.IsNullOrWhiteSpace(adresse))
            throw new EnvoiException($"Aucune adresse {canal} pour ce destinataire.", true);
        if (boite.ConsommerEchec(canal))
            throw new EnvoiException($"Panne simulée du canal {canal}.", boite.EchecDefinitif);
        boite.Envoyes.Enqueue((canal, adresse, message.Contenu));
        logger.LogInformation("[SIMULÉ] {Canal} → {Adresse} : {Contenu}", canal, adresse, message.Contenu);
        return Task.FromResult(new ResultatEnvoi($"simule-{Guid.NewGuid():N}"));
    }
}

/// <summary>E-mail via SMTP (System.Net.Mail).</summary>
public class ExpediteurEmail(IOptions<MessagerieOptions> options) : IExpediteur
{
    public Canal Canal => Canal.Email;

    public async Task<ResultatEnvoi> EnvoyerAsync(Message message, Destinataire d, ModeleMessage? modele, IReadOnlyList<string> parametres, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(d.Email)) throw new EnvoiException("Aucune adresse e-mail pour ce destinataire.", true);
        if (string.IsNullOrWhiteSpace(o.SmtpHote)) throw new EnvoiException("SMTP non configuré (Messagerie:SmtpHote).", true);
        using var mail = new MailMessage(o.ExpediteurEmail, d.Email, "MediaLib — bibliothèque", message.Contenu);
        using var smtp = new SmtpClient(o.SmtpHote, o.SmtpPort) { EnableSsl = o.SmtpSsl };
        if (!string.IsNullOrEmpty(o.SmtpUtilisateur)) smtp.Credentials = new NetworkCredential(o.SmtpUtilisateur, o.SmtpMotDePasse);
        try
        {
            await smtp.SendMailAsync(mail, ct);
        }
        catch (SmtpException e)
        {
            throw new EnvoiException("Envoi SMTP impossible : " + e.Message, false, e);
        }
        return new ResultatEnvoi(null);
    }
}

/// <summary>WhatsApp Business Cloud API (Meta Graph) : modèles hors fenêtre de 24 h, texte libre dans la fenêtre.</summary>
public class ExpediteurWhatsApp(HttpClient http, IOptions<WhatsAppOptions> options) : IExpediteur
{
    public Canal Canal => Canal.WhatsApp;

    public async Task<ResultatEnvoi> EnvoyerAsync(Message message, Destinataire d, ModeleMessage? modele, IReadOnlyList<string> parametres, CancellationToken ct)
    {
        var o = options.Value;
        if (!TelephoneE164Attribute.EstValide(d.Telephone)) throw new EnvoiException("Numéro WhatsApp absent ou invalide.", true);
        if (string.IsNullOrWhiteSpace(o.Jeton) || string.IsNullOrWhiteSpace(o.IdentifiantNumero))
            throw new EnvoiException("WhatsApp n'est pas configuré (WhatsApp:Jeton, WhatsApp:IdentifiantNumero).", true);

        var corps = new JsonObject { ["messaging_product"] = "whatsapp", ["to"] = d.Telephone!.TrimStart('+') };
        if (modele is { } m)
        {
            var valeurs = new JsonArray(parametres.Select(p => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = p }).ToArray());
            corps["type"] = "template";
            corps["template"] = new JsonObject
            {
                ["name"] = m.NomTechnique(),
                ["language"] = new JsonObject { ["code"] = o.LangueModeles },
                ["components"] = parametres.Count == 0 ? new JsonArray() : new JsonArray(new JsonObject { ["type"] = "body", ["parameters"] = valeurs }),
            };
        }
        else
        {
            corps["type"] = "text";
            corps["text"] = new JsonObject { ["preview_url"] = false, ["body"] = message.Contenu };
        }

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{o.VersionApi}/{o.IdentifiantNumero}/messages")
        {
            Content = new StringContent(corps.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Jeton);
        HttpResponseMessage reponse;
        string contenu;
        try
        {
            reponse = await http.SendAsync(requete, ct);
            contenu = await reponse.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new EnvoiException("Meta injoignable : " + e.Message, false, e);
        }

        var code = (int)reponse.StatusCode;
        JsonNode? json = null;
        try { json = JsonNode.Parse(contenu); } catch (JsonException) { }
        if (code >= 400)
        {
            var erreur = json?["error"]?["message"]?.GetValue<string>() ?? $"erreur {code}";
            // 4xx (hors 429) : le message ne passera pas en réessayant.
            throw new EnvoiException("Refus de Meta : " + erreur, code < 500 && code != 429);
        }
        return new ResultatEnvoi(json?["messages"]?[0]?["id"]?.GetValue<string>());
    }
}
