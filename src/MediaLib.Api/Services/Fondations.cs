using System.Text.Json;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;

namespace MediaLib.Api.Services;

public static class HorlogeExtensions
{
    public static DateTime Maintenant(this TimeProvider horloge) => horloge.GetUtcNow().UtcDateTime;

    public static DateOnly Aujourdhui(this TimeProvider horloge) => DateOnly.FromDateTime(horloge.GetUtcNow().UtcDateTime);
}

/// <summary>
/// Politique des actions (port de PolitiqueActions) : chaque action est automatique,
/// soumise à validation humaine ou interdite. Une action inconnue est interdite par défaut.
/// </summary>
public class PolitiqueActions
{
    public const string RappelAvantEcheance = "relance.rappel_avant_echeance";
    public const string RelanceRetardJ1 = "relance.retard_j1";
    public const string RelanceRetardJ7 = "relance.retard_j7";
    public const string NotificationReservation = "notification.reservation";
    public const string Liaison = "liaison.whatsapp";
    public const string ReponseConversation = "reponse.conversation";
    public const string MessageLibre = "message.libre";
    public const string Prolongation = "prolongation";
    public const string Reservation = "reservation";
    public const string Escalade = "escalade";
    public const string RelanceManuelle = "relance.manuelle";
    public const string ConsentementRevoque = "consentement.revoque";
    public const string EmpruntEnregistrer = "emprunt.enregistrer";
    public const string EmpruntRetour = "emprunt.retour";
    public const string CatalogueModifier = "catalogue.modifier";
    public const string DonneesSupprimer = "donnees.supprimer";

    private static readonly Dictionary<string, Classement> Regles = new()
    {
        [RappelAvantEcheance] = Classement.Automatique,       // J-2
        [RelanceRetardJ1] = Classement.Automatique,           // J+1
        [RelanceRetardJ7] = Classement.ValidationRequise,     // J+7
        [RelanceManuelle] = Classement.ValidationRequise,
        [NotificationReservation] = Classement.Automatique,
        [Liaison] = Classement.Automatique,
        [ConsentementRevoque] = Classement.Automatique,
        // La réponse de l'agent dans la conversation qu'il mène est automatique ;
        // un message libre envoyé de sa propre initiative doit être validé.
        [ReponseConversation] = Classement.Automatique,
        [MessageLibre] = Classement.ValidationRequise,
        [Prolongation] = Classement.ValidationRequise,
        [Reservation] = Classement.Automatique,
        [Escalade] = Classement.Automatique,
        [EmpruntEnregistrer] = Classement.Interdite,
        [EmpruntRetour] = Classement.Interdite,
        [CatalogueModifier] = Classement.Interdite,
        [DonneesSupprimer] = Classement.Interdite,
    };

    public Classement Classer(string action) => Regles.GetValueOrDefault(action, Classement.Interdite);

    public static string ActionRelance(NiveauRelance niveau) => niveau switch
    {
        NiveauRelance.RappelAvantEcheance => RappelAvantEcheance,
        NiveauRelance.RelanceRetardJ1 => RelanceRetardJ1,
        _ => RelanceRetardJ7,
    };

    public Classement ClasserRelance(NiveauRelance niveau) => Classer(ActionRelance(niveau));

    public IReadOnlyDictionary<string, Classement> ToutesLesRegles() => Regles;
}

/// <summary>Journal des décisions : chaque action (proposée, retenue, validée) laisse une trace.</summary>
public class JournalDecisions(MediaLibDbContext db, PolitiqueActions politique, TimeProvider horloge)
{
    public DecisionAgent Ouvrir(string declencheur, string action, StatutDecision? statut = null)
    {
        var classement = politique.Classer(action);
        var decision = new DecisionAgent
        {
            Declencheur = declencheur,
            ActionProposee = action,
            Classement = classement,
            Statut = statut ?? classement switch
            {
                Classement.ValidationRequise => StatutDecision.EnAttenteValidation,
                Classement.Interdite => StatutDecision.Interdite,
                _ => StatutDecision.Proposee,
            },
            CreeLe = horloge.Maintenant(),
        };
        db.Decisions.Add(decision);
        return decision;
    }

    public void ChangerStatut(DecisionAgent decision, StatutDecision statut, string? resultat, string? actionRetenue = null)
    {
        decision.Statut = statut;
        if (resultat is not null) decision.Resultat = resultat.Length > 4000 ? resultat[..4000] : resultat;
        if (actionRetenue is not null) decision.ActionRetenue = actionRetenue;
        decision.TraiteLe = horloge.Maintenant();
    }

    public void Valider(DecisionAgent decision, string validateurId, bool approuve, string? commentaire)
    {
        if (decision.Statut != StatutDecision.EnAttenteValidation)
            throw new RegleMetierException("Cette décision n'est pas en attente de validation.");
        decision.ValideParId = validateurId;
        ChangerStatut(decision, approuve ? StatutDecision.Approuvee : StatutDecision.Rejetee,
            commentaire ?? (approuve ? "Approuvée." : "Rejetée."), approuve ? decision.ActionProposee : "aucune");
    }

    public static string Json<T>(T valeur) => JsonSerializer.Serialize(valeur, MediaLibJson.Options);

    public static T? Lire<T>(string json) => JsonSerializer.Deserialize<T>(json, MediaLibJson.Options);
}
