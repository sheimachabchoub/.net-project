using MediaLib.Shared;

namespace MediaLib.Api.Domain;

public class DocumentConnaissance
{
    public int Id { get; set; }
    public string Titre { get; set; } = string.Empty;
    public TypeDocument Type { get; set; }
    public string Version { get; set; } = "1.0";
    public string Contenu { get; set; } = string.Empty;
    public StatutPublication Statut { get; set; } = StatutPublication.Brouillon;
    public DateTime? PublieLe { get; set; }
    public DateTime? IndexeLe { get; set; }
    public List<FragmentConnaissance> Fragments { get; set; } = [];
}

public class FragmentConnaissance
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public DocumentConnaissance Document { get; set; } = null!;
    public string Section { get; set; } = string.Empty;
    public string Contenu { get; set; } = string.Empty;

    /// <summary>Termes normalisés séparés par des espaces (index BM25).</summary>
    public string Termes { get; set; } = string.Empty;
    public int Rang { get; set; }
}

public class Conversation
{
    public const int FenetreHeures = 24;

    public int Id { get; set; }
    public Canal Canal { get; set; }
    public EtatConversation Etat { get; set; } = EtatConversation.Agent;
    public string? UtilisateurId { get; set; }
    public Utilisateur? Utilisateur { get; set; }
    public string? Telephone { get; set; }
    public DateTime OuverteLe { get; set; }
    public DateTime? DernierMessageEntrantLe { get; set; }
    public DateTime DerniereActiviteLe { get; set; }
    public List<Message> Messages { get; set; } = [];

    /// <summary>Fenêtre de service WhatsApp : message libre autorisé 24 h après le dernier message de l'adhérent.</summary>
    public bool FenetreOuverte(DateTime maintenant) =>
        Canal != Canal.WhatsApp
        || (DernierMessageEntrantLe is { } dernier && maintenant - dernier < TimeSpan.FromHours(FenetreHeures));
}

public class Message
{
    public const string AuteurAdherent = "adherent";
    public const string AuteurAgent = "agent";
    public const string AuteurBibliothecaire = "bibliothecaire";
    public const string AuteurSysteme = "systeme";

    public int Id { get; set; }
    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Sens Sens { get; set; }
    public Canal Canal { get; set; }
    public string Auteur { get; set; } = AuteurSysteme;
    public string Contenu { get; set; } = string.Empty;
    public string? Modele { get; set; }
    public string? IdentifiantExterne { get; set; }
    public string? CleIdempotence { get; set; }
    public StatutMessage Statut { get; set; } = StatutMessage.EnAttente;
    public DateTime CreeLe { get; set; }
    public DateTime? EnvoyeLe { get; set; }
    public DateTime? StatutMisAJourLe { get; set; }
    public string? Erreur { get; set; }

    private static int Rang(StatutMessage s) => s switch
    {
        StatutMessage.EnAttente => 0,
        StatutMessage.Envoye => 1,
        StatutMessage.Delivre => 2,
        StatutMessage.Lu => 3,
        _ => 4,
    };

    /// <summary>Applique un statut Meta (sent/delivered/read/failed) sans jamais revenir en arrière.</summary>
    public bool AppliquerStatutExterne(StatutMessage nouveau, DateTime le, string? erreur)
    {
        if (Statut == StatutMessage.Echec) return false;
        if (nouveau != StatutMessage.Echec && Rang(nouveau) <= Rang(Statut)) return false;
        Statut = nouveau;
        StatutMisAJourLe = le;
        if (nouveau == StatutMessage.Echec) Erreur = erreur ?? "Échec signalé par Meta";
        return true;
    }
}

public class Relance
{
    public int Id { get; set; }
    public int EmpruntId { get; set; }
    public Emprunt Emprunt { get; set; } = null!;
    public NiveauRelance Niveau { get; set; }

    /// <summary>Échéance au moment de la détection : une prolongation crée une nouvelle relance distincte.</summary>
    public DateOnly Echeance { get; set; }
    public StatutRelance Statut { get; set; } = StatutRelance.Planifiee;
    public DateTime PrevueLe { get; set; }
    public string CleIdempotence { get; set; } = string.Empty;
    public int Tentatives { get; set; }
    public DateTime? DerniereTentativeLe { get; set; }
    public int? MessageId { get; set; }
    public Message? Message { get; set; }
    public int? DecisionId { get; set; }
    public DecisionAgent? Decision { get; set; }

    public static string Cle(int empruntId, NiveauRelance niveau, DateOnly echeance) =>
        $"relance-{empruntId}-{niveau}-{echeance:yyyyMMdd}";
}

/// <summary>Journal des décisions (agent, planificateur, bibliothécaire).</summary>
public class DecisionAgent
{
    public int Id { get; set; }
    public string Declencheur { get; set; } = string.Empty;
    public string ActionProposee { get; set; } = string.Empty;
    public string? ActionRetenue { get; set; }
    public Classement Classement { get; set; }
    public string? Motif { get; set; }

    /// <summary>JSON : liste des outils consultés.</summary>
    public string OutilsJson { get; set; } = "[]";

    /// <summary>JSON : sources citées (titre, version, section).</summary>
    public string SourcesJson { get; set; } = "[]";

    /// <summary>JSON : données utiles à l'exécution (ids, échéances…).</summary>
    public string DonneesJson { get; set; } = "{}";
    public StatutDecision Statut { get; set; }
    public string? Resultat { get; set; }
    public DateTime CreeLe { get; set; }
    public DateTime? TraiteLe { get; set; }

    public string? DemandeurId { get; set; }
    public Utilisateur? Demandeur { get; set; }
    public int? EmpruntId { get; set; }
    public Emprunt? Emprunt { get; set; }
    public int? ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public int? MessageId { get; set; }
    public Message? Message { get; set; }
    public string? ValideParId { get; set; }
    public Utilisateur? ValidePar { get; set; }
}
