using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Communication;

/// <summary>
/// Point d'entrée unique des messages : choix du canal, idempotence, historique, statuts,
/// fenêtre WhatsApp de 24 h et repli e-mail.
/// </summary>
public class ServiceCommunication(
    MediaLibDbContext db,
    IEnumerable<IExpediteur> expediteurs,
    ExpediteurSimule simule,
    IOptions<MessagerieOptions> options,
    TimeProvider horloge,
    ILogger<ServiceCommunication> logger)
{
    public bool EstSimule => !string.Equals(options.Value.Transport, "reel", StringComparison.OrdinalIgnoreCase);

    public static Canal CanalPour(Utilisateur u) =>
        u.CanalPrefere == Canal.WhatsApp && u.PeutRecevoirWhatsapp ? Canal.WhatsApp : Canal.Email;

    /// <summary>
    /// Notification sortante à partir d'un modèle. Une même clé d'idempotence ne produit jamais deux envois réussis.
    /// En cas d'échec WhatsApp, repli automatique sur l'e-mail.
    /// </summary>
    public async Task<Message> NotifierAsync(Utilisateur u, ModeleMessage modele, IReadOnlyList<string> parametres, string cleIdempotence, CancellationToken ct = default)
    {
        var existant = await db.Messages.FirstOrDefaultAsync(m => m.CleIdempotence == cleIdempotence, ct);
        if (existant is not null && existant.Statut is not (StatutMessage.Echec or StatutMessage.EnAttente))
            return existant;

        var canal = CanalPour(u);
        var message = existant;
        if (message is null)
        {
            var conversation = await ConversationPourAsync(u, canal, ct);
            message = CreerSortant(conversation, canal, Message.AuteurSysteme, modele.Texte(parametres));
            message.Modele = modele.NomTechnique();
            message.CleIdempotence = cleIdempotence;
        }
        else
        {
            message.Canal = canal;
            message.Erreur = null;
        }

        var destinataire = new Destinataire(u.Email, u.Telephone, u.NomComplet);
        try
        {
            await ExpedierAsync(message, destinataire, modele, parametres, ct);
        }
        catch (EnvoiException e) when (message.Canal == Canal.WhatsApp && !string.IsNullOrEmpty(u.Email))
        {
            logger.LogWarning("WhatsApp indisponible ({Erreur}) : repli sur l'e-mail.", e.Message);
            message.Canal = Canal.Email;
            message.Erreur = null;
            message.Statut = StatutMessage.EnAttente;
            await ExpedierAsync(message, destinataire, modele, parametres, ct);
        }
        return message;
    }

    /// <summary>Réponse libre dans une conversation (agent, bibliothécaire ou système).</summary>
    public async Task<Message> RepondreAsync(Conversation conversation, string texte, string auteur, CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        if (conversation.Canal == Canal.WhatsApp && !conversation.FenetreOuverte(maintenant))
            throw new FenetreFermeeException();

        var message = CreerSortant(conversation, conversation.Canal, auteur, texte);
        if (conversation.Canal == Canal.Web)
        {
            // Affiché directement dans la discussion Blazor : aucun transport externe.
            message.Statut = StatutMessage.Envoye;
            message.EnvoyeLe = maintenant;
            await db.SaveChangesAsync(ct);
            return message;
        }

        Utilisateur? u = conversation.UtilisateurId is null ? null : await db.Users.FindAsync([conversation.UtilisateurId], ct);
        var destinataire = new Destinataire(u?.Email, conversation.Telephone ?? u?.Telephone, u?.NomComplet);
        await ExpedierAsync(message, destinataire, null, [], ct);
        return message;
    }

    public async Task<Message> EnregistrerEntrantAsync(Conversation conversation, string texte, string? identifiantExterne, CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        var message = new Message
        {
            Conversation = conversation,
            Sens = Sens.Entrant,
            Canal = conversation.Canal,
            Auteur = Message.AuteurAdherent,
            Contenu = texte,
            IdentifiantExterne = identifiantExterne,
            Statut = StatutMessage.Recu,
            CreeLe = maintenant,
        };
        conversation.DernierMessageEntrantLe = maintenant;
        conversation.DerniereActiviteLe = maintenant;
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task<Conversation> ConversationPourAsync(Utilisateur u, Canal canal, CancellationToken ct = default)
    {
        var conversation = await db.Conversations
            .Where(c => c.Canal == canal && c.UtilisateurId == u.Id && c.Etat != EtatConversation.Close)
            .OrderByDescending(c => c.DerniereActiviteLe).FirstOrDefaultAsync(ct);
        if (conversation is not null) return conversation;
        var maintenant = horloge.Maintenant();
        conversation = new Conversation
        {
            Canal = canal,
            UtilisateurId = u.Id,
            Telephone = canal == Canal.WhatsApp ? u.Telephone : null,
            OuverteLe = maintenant,
            DerniereActiviteLe = maintenant,
        };
        db.Conversations.Add(conversation);
        return conversation;
    }

    private Message CreerSortant(Conversation conversation, Canal canal, string auteur, string texte)
    {
        var maintenant = horloge.Maintenant();
        conversation.DerniereActiviteLe = maintenant;
        var message = new Message
        {
            Conversation = conversation,
            Sens = Sens.Sortant,
            Canal = canal,
            Auteur = auteur,
            Contenu = texte,
            Statut = StatutMessage.EnAttente,
            CreeLe = maintenant,
        };
        db.Messages.Add(message);
        return message;
    }

    private async Task ExpedierAsync(Message message, Destinataire destinataire, ModeleMessage? modele, IReadOnlyList<string> parametres, CancellationToken ct)
    {
        try
        {
            ResultatEnvoi resultat;
            if (EstSimule)
                resultat = await simule.EnvoyerAsync(message.Canal, message, destinataire, ct);
            else
            {
                var expediteur = expediteurs.FirstOrDefault(e => e.Canal == message.Canal)
                    ?? throw new EnvoiException($"Aucun expéditeur pour le canal {message.Canal}.", true);
                resultat = await expediteur.EnvoyerAsync(message, destinataire, modele, parametres, ct);
            }
            message.Statut = StatutMessage.Envoye;
            message.EnvoyeLe = horloge.Maintenant();
            message.IdentifiantExterne = resultat.IdentifiantExterne;
            message.Erreur = null;
            await db.SaveChangesAsync(ct);
        }
        catch (EnvoiException e)
        {
            message.Statut = StatutMessage.Echec;
            message.Erreur = e.Message;
            message.StatutMisAJourLe = horloge.Maintenant();
            await db.SaveChangesAsync(ct);
            throw;
        }
    }
}
