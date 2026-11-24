namespace MediaLib.Shared;

/// <summary>Noms des rôles Identity utilisés partout (API et Blazor).</summary>
public static class Roles
{
    public const string Membre = "Membre";
    public const string Bibliothecaire = "Bibliothecaire";
    public const string Admin = "Admin";

    /// <summary>Rôles autorisés à gérer le catalogue, les retours et les retards.</summary>
    public const string Gestion = Bibliothecaire + "," + Admin;

    public static readonly string[] Tous = [Membre, Bibliothecaire, Admin];
}

public enum StatutEmprunt { EnCours, EcheanceProche, EnRetard, Rendu }

public enum StatutReservation { EnAttente, Notifiee, Honoree, Expiree, Annulee }

public enum StatutPublication { Brouillon, Publie, Archive }

public enum TypeDocument { Reglement, ConditionsPret, Faq, Horaires, CharteWhatsapp, Autre }

public enum Canal { Web, WhatsApp, Email, Simule }

public enum Sens { Entrant, Sortant }

public enum StatutMessage { EnAttente, Envoye, Delivre, Lu, Echec, Recu }

public enum EtatConversation { Agent, Humain, Close }

public enum StatutDecision { Proposee, EnAttenteValidation, Approuvee, Rejetee, Executee, Annulee, Escaladee, Interdite }

/// <summary>Classement d'une action par la politique (équivalent PolitiqueActions Symfony).</summary>
public enum Classement { Automatique, ValidationRequise, Interdite }

public enum NiveauRelance { RappelAvantEcheance, RelanceRetardJ1, RelanceRetardJ7 }

public enum StatutRelance { Planifiee, EnAttenteValidation, Envoyee, Annulee, Echec, Escaladee }

public enum TriLivres { Titre, TitreDesc, Annee, AnneeDesc, Auteur, Disponibles }
