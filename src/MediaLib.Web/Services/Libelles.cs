using MediaLib.Shared;

namespace MediaLib.Web.Services;

/// <summary>Libellés français des enums affichés dans l'interface.</summary>
public static class Libelles
{
    public static string Emprunt(StatutEmprunt s) => s switch
    {
        StatutEmprunt.EnCours => "En cours",
        StatutEmprunt.EcheanceProche => "Échéance proche",
        StatutEmprunt.EnRetard => "En retard",
        _ => "Rendu",
    };

    public static string Reservation(StatutReservation s) => s switch
    {
        StatutReservation.EnAttente => "En file d'attente",
        StatutReservation.Notifiee => "Mis de côté pour vous",
        StatutReservation.Honoree => "Honorée",
        StatutReservation.Expiree => "Expirée",
        _ => "Annulée",
    };

    public static string Decision(StatutDecision s) => s switch
    {
        StatutDecision.Proposee => "Proposée",
        StatutDecision.EnAttenteValidation => "À valider",
        StatutDecision.Approuvee => "Approuvée",
        StatutDecision.Rejetee => "Rejetée",
        StatutDecision.Executee => "Exécutée",
        StatutDecision.Annulee => "Annulée",
        StatutDecision.Escaladee => "Escaladée",
        _ => "Interdite",
    };

    public static string Tri(TriLivres t) => t switch
    {
        TriLivres.Titre => "Titre (A → Z)",
        TriLivres.TitreDesc => "Titre (Z → A)",
        TriLivres.Annee => "Année (ancien d'abord)",
        TriLivres.AnneeDesc => "Année (récent d'abord)",
        TriLivres.Auteur => "Auteur",
        _ => "Disponibilité",
    };

    public static string Moteur(string m) => m switch
    {
        "claude" => "Claude (API réelle)",
        "simule" => "moteur simulé",
        "simule:sans-cle" => "moteur simulé (clé API absente)",
        "simule:panne" => "moteur simulé (API indisponible)",
        "simule:plafond" => "moteur simulé (plafond quotidien atteint)",
        "simule:refus" => "moteur simulé (refus du modèle)",
        "limite" => "limitation de débit",
        "humain" => "bibliothécaire",
        _ => m,
    };
}
