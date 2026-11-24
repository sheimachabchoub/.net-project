using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaLib.Api.Agent;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Communication;

public record EvenementMessage(string IdentifiantMeta, string De, string Texte, long Horodatage);

public record EvenementStatut(string IdentifiantMeta, string Statut, long Horodatage, string? Erreur);

/// <summary>Analyse et authentification des appels du webhook Meta (WhatsApp Cloud API).</summary>
public class AnalyseurWebhookWhatsApp(IOptions<WhatsAppOptions> options)
{
    /// <summary>Vérifie X-Hub-Signature-256 = « sha256= » + HMAC-SHA256(corps brut, secret d'application).</summary>
    public bool SignatureValide(string corps, string? signature)
    {
        var secret = options.Value.SecretApplication;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || !signature.StartsWith("sha256=", StringComparison.Ordinal))
            return false;
        var attendue = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(corps)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(attendue), Encoding.ASCII.GetBytes(signature));
    }

    public bool AbonnementValide(string? mode, string? jeton)
    {
        var attendu = options.Value.JetonVerification;
        return mode == "subscribe" && !string.IsNullOrEmpty(attendu) && jeton is not null
               && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(attendu), Encoding.UTF8.GetBytes(jeton));
    }

    public (List<EvenementMessage> Messages, List<EvenementStatut> Statuts) Analyser(string corps)
    {
        var messages = new List<EvenementMessage>();
        var statuts = new List<EvenementStatut>();
        using var doc = JsonDocument.Parse(corps);
        foreach (var entree in Tableau(doc.RootElement, "entry"))
            foreach (var changement in Tableau(entree, "changes"))
            {
                if (!changement.TryGetProperty("value", out var valeur)) continue;
                foreach (var m in Tableau(valeur, "messages"))
                    messages.Add(new EvenementMessage(Texte(m, "id") ?? Guid.NewGuid().ToString("N"), Texte(m, "from") ?? string.Empty, ContenuTexte(m), Long(m, "timestamp")));
                foreach (var s in Tableau(valeur, "statuses"))
                {
                    string? erreur = null;
                    if (s.TryGetProperty("errors", out var erreurs) && erreurs.ValueKind == JsonValueKind.Array && erreurs.GetArrayLength() > 0)
                        erreur = Texte(erreurs[0], "title") ?? Texte(erreurs[0], "message");
                    statuts.Add(new EvenementStatut(Texte(s, "id") ?? string.Empty, Texte(s, "status") ?? string.Empty, Long(s, "timestamp"), erreur));
                }
            }
        return (messages, statuts);
    }

    private static IEnumerable<JsonElement> Tableau(JsonElement e, string nom) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out var t) && t.ValueKind == JsonValueKind.Array ? t.EnumerateArray() : [];

    private static string? Texte(JsonElement e, string nom) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Long(JsonElement e, string nom) => long.TryParse(Texte(e, nom), out var l) ? l : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string ContenuTexte(JsonElement m) => (Texte(m, "type") ?? "text") switch
    {
        "text" => m.TryGetProperty("text", out var t) ? Texte(t, "body") ?? string.Empty : string.Empty,
        "button" => m.TryGetProperty("button", out var b) ? Texte(b, "text") ?? string.Empty : string.Empty,
        "interactive" => m.TryGetProperty("interactive", out var i)
            ? (i.TryGetProperty("button_reply", out var br) ? Texte(br, "title") : i.TryGetProperty("list_reply", out var lr) ? Texte(lr, "title") : null) ?? string.Empty
            : string.Empty,
        _ => string.Empty,
    };
}

/// <summary>Traitement d'un message WhatsApp entrant : STOP, liaison, confirmation « OUI », reprise humaine, agent.</summary>
public class TraitementMessageEntrant(
    MediaLibDbContext db,
    UserManager<Utilisateur> users,
    ServiceCommunication communication,
    LiaisonWhatsApp liaison,
    DemandesAdherent demandes,
    BibliothecaireIA agent,
    JournalDecisions journal,
    TimeProvider horloge,
    ILogger<TraitementMessageEntrant> logger)
{
    private static readonly HashSet<string> MotsStop = ["stop", "arret", "arrêt", "desinscription", "désinscription", "unsubscribe"];
    private static readonly HashSet<string> MotsOui = ["oui", "ok", "confirmer", "je confirme", "yes"];

    public async Task MessageRecuAsync(EvenementMessage evt, CancellationToken ct = default)
    {
        // Idempotence : Meta peut rejouer un même événement.
        if (await db.Messages.AnyAsync(m => m.IdentifiantExterne == evt.IdentifiantMeta, ct)) return;

        var telephone = "+" + evt.De.TrimStart('+');
        var adherent = await db.Users.FirstOrDefaultAsync(u => u.Telephone == telephone, ct);
        if (adherent is { TelephoneVerifieLe: null }) adherent = null;

        var conversation = await db.Conversations.Where(c => c.Canal == Canal.WhatsApp && c.Telephone == telephone)
            .OrderByDescending(c => c.DerniereActiviteLe).FirstOrDefaultAsync(ct);
        if (conversation is null)
        {
            var maintenant = horloge.Maintenant();
            conversation = new Conversation { Canal = Canal.WhatsApp, Telephone = telephone, UtilisateurId = adherent?.Id, OuverteLe = maintenant, DerniereActiviteLe = maintenant };
            db.Conversations.Add(conversation);
        }
        else
        {
            if (adherent is not null && conversation.UtilisateurId is null) conversation.UtilisateurId = adherent.Id;
            if (conversation.Etat == EtatConversation.Close) conversation.Etat = EtatConversation.Agent;
        }

        await communication.EnregistrerEntrantAsync(conversation, evt.Texte.Length == 0 ? "[contenu non textuel]" : evt.Texte, evt.IdentifiantMeta, ct);
        var normalise = evt.Texte.Trim().Trim('.', '!', ' ').ToLowerInvariant();

        // STOP révoque immédiatement le consentement : plus aucun envoi WhatsApp.
        if (MotsStop.Contains(normalise))
        {
            await RevoquerAsync(conversation, adherent, ct);
            return;
        }
        if (LiaisonWhatsApp.ExtraireCode(evt.Texte) is { } code)
        {
            await LierAsync(conversation, code, evt.De, ct);
            return;
        }
        if (adherent is not null && MotsOui.Contains(normalise) && await ConfirmerReservationAsync(conversation, adherent, ct)) return;
        if (conversation.Etat == EtatConversation.Humain) return; // un bibliothécaire a repris la main : l'agent se tait

        var roles = adherent is null ? [] : (await users.GetRolesAsync(adherent)).ToList();
        await agent.RepondreAsync(conversation, evt.Texte, adherent, roles, "Webhook WhatsApp", ct);
    }

    public async Task StatutRecuAsync(EvenementStatut evt, CancellationToken ct = default)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.IdentifiantExterne == evt.IdentifiantMeta, ct);
        StatutMessage? nouveau = evt.Statut switch
        {
            "sent" => StatutMessage.Envoye,
            "delivered" => StatutMessage.Delivre,
            "read" => StatutMessage.Lu,
            "failed" => StatutMessage.Echec,
            _ => null,
        };
        if (message is null || nouveau is null) return;
        if (!message.AppliquerStatutExterne(nouveau.Value, DateTimeOffset.FromUnixTimeSeconds(evt.Horodatage).UtcDateTime, evt.Erreur)) return;

        // Un échec de livraison d'une relance (numéro invalide…) est escaladé au bibliothécaire.
        if (nouveau == StatutMessage.Echec)
        {
            var relance = await db.Relances.Include(r => r.Decision).FirstOrDefaultAsync(r => r.MessageId == message.Id, ct);
            if (relance is not null)
            {
                relance.Statut = StatutRelance.Escaladee;
                if (relance.Decision is { } d) journal.ChangerStatut(d, StatutDecision.Escaladee, "Échec de livraison signalé par Meta : " + (evt.Erreur ?? "inconnu"));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task RevoquerAsync(Conversation conversation, Utilisateur? adherent, CancellationToken ct)
    {
        if (adherent is not null) await liaison.RevoquerConsentementAsync(adherent, ct);
        var d = journal.Ouvrir("Webhook WhatsApp : mot-clé STOP", PolitiqueActions.ConsentementRevoque, StatutDecision.Executee);
        d.DemandeurId = adherent?.Id;
        d.Conversation = conversation;
        d.Resultat = adherent is not null ? "Consentement WhatsApp révoqué." : "Numéro non lié : rien à révoquer.";
        d.TraiteLe = horloge.Maintenant();
        conversation.Etat = EtatConversation.Close;
        await db.SaveChangesAsync(ct);
    }

    private async Task LierAsync(Conversation conversation, string code, string de, CancellationToken ct)
    {
        var adherent = await liaison.VerifierAsync(code, de, ct);
        var d = journal.Ouvrir("Webhook WhatsApp : code de liaison", PolitiqueActions.Liaison);
        d.Conversation = conversation;
        if (adherent is null)
        {
            journal.ChangerStatut(d, StatutDecision.Annulee, "Code inconnu ou expiré.");
            await RepondreAsync(conversation, "Ce code de liaison est invalide ou a expiré. Générez un nouveau code depuis « Mon profil » sur le site.", ct);
            await db.SaveChangesAsync(ct);
            return;
        }
        conversation.UtilisateurId = adherent.Id;
        conversation.Telephone = adherent.Telephone;
        d.DemandeurId = adherent.Id;
        var message = await RepondreAsync(conversation, ModeleMessage.LiaisonConfirmee.Texte([adherent.Prenom]), ct);
        d.MessageId = message?.Id;
        journal.ChangerStatut(d, StatutDecision.Executee, $"Numéro {adherent.Telephone} vérifié.", PolitiqueActions.Liaison);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ConfirmerReservationAsync(Conversation conversation, Utilisateur adherent, CancellationToken ct)
    {
        var proposition = await demandes.PropositionEnAttenteAsync(adherent.Id, horloge.Maintenant().AddHours(-24), ct);
        if (proposition is null) return false;
        try
        {
            var reservation = await demandes.ConfirmerReservationAsync(proposition.Id, adherent.Id, ct);
            var titre = await db.Livres.Where(l => l.Id == reservation.LivreId).Select(l => l.Titre).FirstAsync(ct);
            await RepondreAsync(conversation, $"C'est noté : « {titre} » est réservé pour vous. Nous vous préviendrons dès son retour.", ct);
        }
        catch (Exception e) when (e is RegleMetierException or IntrouvableException)
        {
            await RepondreAsync(conversation, "Réservation impossible : " + e.Message, ct);
        }
        return true;
    }

    private async Task<Message?> RepondreAsync(Conversation conversation, string texte, CancellationToken ct)
    {
        try
        {
            return await communication.RepondreAsync(conversation, texte, Message.AuteurSysteme, ct);
        }
        catch (EnvoiException e)
        {
            logger.LogError("Réponse WhatsApp impossible : {Erreur}", e.Message);
            return null;
        }
    }
}
