namespace MediaLib.Api.Services;

/// <summary>Violation d'une règle métier : traduite en 409 Conflict par l'API.</summary>
public class RegleMetierException(string message) : Exception(message);

/// <summary>Ressource introuvable : traduite en 404.</summary>
public class IntrouvableException(string message) : Exception(message);

/// <summary>Action refusée à l'utilisateur courant : traduite en 403.</summary>
public class AccesRefuseException(string message) : Exception(message);

/// <summary>Échec d'envoi d'un message (définitif = inutile de réessayer).</summary>
public class EnvoiException(string message, bool definitif, Exception? interne = null) : Exception(message, interne)
{
    public bool Definitif { get; } = definitif;
}

/// <summary>Fenêtre WhatsApp de 24 h fermée : seul un modèle approuvé peut être envoyé.</summary>
public class FenetreFermeeException()
    : EnvoiException("La fenêtre de 24 h WhatsApp est fermée : seul un message modèle peut être envoyé.", true);
