using System.ComponentModel.DataAnnotations;

namespace MediaLib.Api.Configuration;

/// <summary>Règles métier configurables (section « Bibliotheque » d'appsettings).</summary>
public class ReglesBibliotheque
{
    public const string Section = "Bibliotheque";

    [Range(1, 50)] public int MaxEmprunts { get; set; } = 5;
    [Range(1, 365)] public int DureeJours { get; set; } = 21;
    /// <summary>Rappel envoyé J-n avant l'échéance (J-2 par défaut).</summary>
    [Range(0, 30)] public int JoursAvantEcheance { get; set; } = 2;
    [Range(1, 60)] public int ProlongationJours { get; set; } = 7;
    [Range(0, 10)] public int ProlongationsMax { get; set; } = 1;
    [Range(1, 30)] public int DelaiRetraitReservationJours { get; set; } = 3;
    /// <summary>Nombre maximal de tentatives d'envoi d'une relance avant escalade.</summary>
    [Range(1, 10)] public int TentativesMax { get; set; } = 3;
    /// <summary>Relance automatique J+n après l'échéance.</summary>
    [Range(1, 30)] public int RelanceRetardJours { get; set; } = 1;
    /// <summary>Relance soumise à validation humaine J+n après l'échéance.</summary>
    [Range(2, 60)] public int RelanceValidationJours { get; set; } = 7;
}

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Emetteur { get; set; } = "MediaLib.Api";
    [Required] public string Audience { get; set; } = "MediaLib.Web";
    /// <summary>Clé HMAC-SHA256 (≥ 32 octets). En dehors du développement, la fournir via user-secrets ou variable d'environnement.</summary>
    [Required, MinLength(32)] public string Cle { get; set; } = string.Empty;
    [Range(1, 120)] public int DureeAccesMinutes { get; set; } = 15;
    [Range(1, 90)] public int DureeRefreshJours { get; set; } = 7;
}

public class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>« simule » (hors ligne, par défaut) ou « claude » (API Anthropic réelle).</summary>
    public string Mode { get; set; } = "simule";
    /// <summary>Clé API Anthropic : user-secrets ou variable d'environnement Agent__CleApi / ANTHROPIC_API_KEY.</summary>
    public string? CleApi { get; set; }
    public string Modele { get; set; } = "claude-opus-5-5";
    [Range(1, 1000)] public int MessagesParMinute { get; set; } = 10;
    [Range(0, 100000)] public int PlafondQuotidien { get; set; } = 200;
    [Range(256, 64000)] public int MaxTokens { get; set; } = 4096;
    [Range(5, 300)] public int DelaiSecondes { get; set; } = 60;
}

public class MessagerieOptions
{
    public const string Section = "Messagerie";

    /// <summary>« simule » (aucun envoi réel) ou « reel ».</summary>
    public string Transport { get; set; } = "simule";
    public string ExpediteurEmail { get; set; } = "bibliotheque@medialib.local";
    public string? SmtpHote { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUtilisateur { get; set; }
    public string? SmtpMotDePasse { get; set; }
    public bool SmtpSsl { get; set; } = true;
}

public class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public string? Jeton { get; set; }
    public string? IdentifiantNumero { get; set; }
    public string VersionApi { get; set; } = "v21.0";
    public string LangueModeles { get; set; } = "fr";
    /// <summary>Jeton de vérification du webhook (GET hub.verify_token).</summary>
    public string? JetonVerification { get; set; }
    /// <summary>Secret d'application Meta pour vérifier X-Hub-Signature-256.</summary>
    public string? SecretApplication { get; set; }
    /// <summary>Numéro public de la bibliothèque (lien wa.me).</summary>
    public string NumeroPublic { get; set; } = "+21600000000";
}

public class PlanificateurOptions
{
    public const string Section = "Planificateur";

    public bool Actif { get; set; } = true;
    [Range(1, 1440)] public int IntervalleMinutes { get; set; } = 60;
}
