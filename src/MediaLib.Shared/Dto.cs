using System.ComponentModel.DataAnnotations;

namespace MediaLib.Shared;

// ---------------------------------------------------------------- Authentification

public class InscriptionRequete
{
    [Required(ErrorMessage = "L'e-mail est obligatoire."), EmailAddress(ErrorMessage = "E-mail invalide."), StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le prénom est obligatoire."), StringLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le nom est obligatoire."), StringLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Au moins 8 caractères.")]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*\d).{8,}$", ErrorMessage = "Au moins 8 caractères, une majuscule et un chiffre.")]
    public string MotDePasse { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirmez le mot de passe."), Compare(nameof(MotDePasse), ErrorMessage = "Les mots de passe ne correspondent pas.")]
    public string ConfirmationMotDePasse { get; set; } = string.Empty;
}

public class ConnexionRequete
{
    [Required(ErrorMessage = "L'e-mail est obligatoire."), EmailAddress(ErrorMessage = "E-mail invalide.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string MotDePasse { get; set; } = string.Empty;
}

public class RefreshRequete
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public record UtilisateurDto(
    string Id, string Email, string Prenom, string Nom, IReadOnlyList<string> Roles,
    string? Telephone, bool TelephoneVerifie, Canal CanalPrefere, bool ConsentementWhatsapp);

public record JetonsReponse(string AccessToken, DateTime ExpireLe, string RefreshToken, DateTime RefreshExpireLe, UtilisateurDto Utilisateur);

// ---------------------------------------------------------------- Compte / profil

public class ProfilRequete
{
    [Required, StringLength(100)] public string Prenom { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Nom { get; set; } = string.Empty;
    [TelephoneE164] public string? Telephone { get; set; }
    public Canal CanalPrefere { get; set; } = Canal.Email;
}

public class ConsentementRequete
{
    public bool Consentement { get; set; }
}

public record LiaisonWhatsappDto(string Code, string Lien, DateTime ExpireLe);

// ---------------------------------------------------------------- Catalogue

public record PageResultat<T>(IReadOnlyList<T> Elements, int Page, int TaillePage, int Total)
{
    public int NbPages => TaillePage <= 0 ? 0 : (int)Math.Ceiling(Total / (double)TaillePage);
}

public record LivreDto(
    int Id, string Titre, string Isbn, string? Resume, int Annee, int NbExemplaires, int NbDisponibles,
    string? Couverture, int AuteurId, string AuteurNom, int CategorieId, string CategorieNom);

public class LivreRequete
{
    [Required(ErrorMessage = "Le titre est obligatoire."), StringLength(200)]
    public string Titre { get; set; } = string.Empty;

    [Required(ErrorMessage = "L'ISBN est obligatoire."), Isbn]
    public string Isbn { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Resume { get; set; }

    [Range(1450, 2100, ErrorMessage = "Année entre 1450 et 2100.")]
    public int Annee { get; set; } = DateTime.Today.Year;

    [Range(1, 1000, ErrorMessage = "Au moins un exemplaire.")]
    public int NbExemplaires { get; set; } = 1;

    [Url(ErrorMessage = "La couverture doit être une URL."), StringLength(500)]
    public string? Couverture { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choisissez un auteur.")]
    public int AuteurId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choisissez une catégorie.")]
    public int CategorieId { get; set; }
}

public record AuteurDto(int Id, string Prenom, string Nom, string? Biographie, int NbLivres)
{
    public string NomComplet => $"{Prenom} {Nom}".Trim();
}

public class AuteurRequete
{
    [StringLength(100)] public string Prenom { get; set; } = string.Empty;
    [Required(ErrorMessage = "Le nom est obligatoire."), StringLength(100)] public string Nom { get; set; } = string.Empty;
    [StringLength(2000)] public string? Biographie { get; set; }
}

public record CategorieDto(int Id, string Nom, string? Description, int NbLivres);

public class CategorieRequete
{
    [Required(ErrorMessage = "Le nom est obligatoire."), StringLength(100)] public string Nom { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
}

// ---------------------------------------------------------------- Emprunts et réservations

public class EmpruntRequete
{
    [Range(1, int.MaxValue)] public int LivreId { get; set; }

    /// <summary>Réservé au bibliothécaire : enregistrer un prêt pour un adhérent. Ignoré pour un membre.</summary>
    public string? UtilisateurId { get; set; }
}

public record EmpruntDto(
    int Id, int LivreId, string LivreTitre, string UtilisateurId, string UtilisateurNom,
    DateOnly DateEmprunt, DateOnly DateRetourPrevue, DateOnly? DateRetourReelle, int NbProlongations,
    StatutEmprunt Statut, int JoursRetard);

public class ReservationRequete
{
    [Range(1, int.MaxValue)] public int LivreId { get; set; }
}

public record ReservationDto(int Id, int LivreId, string LivreTitre, StatutReservation Statut, DateTime CreeLe, DateTime? ExpireLe, int Position);

public record ReglesDto(int MaxEmprunts, int DureeJours, int JoursAvantEcheance, int ProlongationJours, int ProlongationsMax, int DelaiRetraitReservationJours, int TentativesMax);

// ---------------------------------------------------------------- Agent IA et conversations

public class QuestionAgentRequete
{
    [Required(ErrorMessage = "Écrivez une question."), StringLength(2000)]
    public string Texte { get; set; } = string.Empty;

    public int? ConversationId { get; set; }
}

public record SourceDto(string Titre, string Version, string Section);

public record ReponseAgentDto(
    int ConversationId, string Texte, IReadOnlyList<string> Outils, IReadOnlyList<SourceDto> Sources,
    bool Escalade, string Moteur, int? DecisionId, int? PropositionReservationId);

public record ConversationDto(
    int Id, Canal Canal, EtatConversation Etat, string? UtilisateurNom, string? Telephone,
    DateTime OuverteLe, DateTime DerniereActiviteLe, bool FenetreOuverte, int NbMessages);

public record MessageDto(int Id, Sens Sens, Canal Canal, string Auteur, string Contenu, StatutMessage Statut, DateTime CreeLe, string? Erreur, string? CleIdempotence);

public record ConversationDetailDto(ConversationDto Conversation, IReadOnlyList<MessageDto> Messages);

public class ReponseBibliothecaireRequete
{
    [Required(ErrorMessage = "Le message est vide."), StringLength(2000)]
    public string Texte { get; set; } = string.Empty;
}

// ---------------------------------------------------------------- Décisions

public record DecisionDto(
    int Id, string Declencheur, string ActionProposee, string? ActionRetenue, Classement Classement, string? Motif,
    IReadOnlyList<string> Outils, IReadOnlyList<SourceDto> Sources, string Donnees, StatutDecision Statut,
    string? Resultat, string? Validateur, string? Demandeur, int? EmpruntId, int? ConversationId,
    DateTime CreeLe, DateTime? TraiteLe);

public class ValidationDecisionRequete
{
    [StringLength(1000)] public string? Commentaire { get; set; }
}

// ---------------------------------------------------------------- Documents de connaissance (RAG)

public record DocumentDto(
    int Id, string Titre, TypeDocument Type, string Version, string Contenu, StatutPublication Statut,
    DateTime? PublieLe, DateTime? IndexeLe, int NbFragments);

public class DocumentRequete
{
    [Required(ErrorMessage = "Le titre est obligatoire."), StringLength(200)] public string Titre { get; set; } = string.Empty;
    public TypeDocument Type { get; set; } = TypeDocument.Autre;
    [Required, StringLength(20), RegularExpression(@"^\d+(\.\d+)*$", ErrorMessage = "Version du type 1.0")] public string Version { get; set; } = "1.0";
    [Required(ErrorMessage = "Le contenu est obligatoire.")] public string Contenu { get; set; } = string.Empty;
}

public record ExtraitDto(int DocumentId, string Titre, string Version, string Section, string Contenu, double Score)
{
    public string Citation => $"{Titre}, version {Version}, section « {Section} »";
}

// ---------------------------------------------------------------- Relances

public record RelanceDto(
    int Id, int EmpruntId, string LivreTitre, string Adherent, NiveauRelance Niveau, DateOnly Echeance,
    StatutRelance Statut, int Tentatives, string CleIdempotence, DateTime PrevueLe, int? DecisionId);

public record TraitementRelancesDto(int Detectees, int Envoyees, int EnEchec, int ReservationsExpirees);

// ---------------------------------------------------------------- Administration

public record UtilisateurAdminDto(string Id, string Email, string NomComplet, IReadOnlyList<string> Roles, bool Verrouille, DateTimeOffset? VerrouilleJusqua, int EmpruntsEnCours);

public class RolesRequete
{
    [Required, MinLength(1, ErrorMessage = "Au moins un rôle.")]
    public List<string> Roles { get; set; } = [];
}
