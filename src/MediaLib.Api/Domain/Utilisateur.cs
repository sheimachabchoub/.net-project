using MediaLib.Shared;
using Microsoft.AspNetCore.Identity;

namespace MediaLib.Api.Domain;

/// <summary>Utilisateur Identity enrichi (adhérent, bibliothécaire ou administrateur).</summary>
public class Utilisateur : IdentityUser
{
    public string Prenom { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public DateTime InscritLe { get; set; }

    /// <summary>Numéro WhatsApp au format E.164 (+216…), unique quand il est renseigné.</summary>
    public string? Telephone { get; set; }
    public DateTime? TelephoneVerifieLe { get; set; }
    public Canal CanalPrefere { get; set; } = Canal.Email;
    public DateTime? ConsentementWhatsappLe { get; set; }
    public string? CodeLiaison { get; set; }
    public DateTime? CodeLiaisonExpireLe { get; set; }

    public List<Emprunt> Emprunts { get; set; } = [];

    public string NomComplet => $"{Prenom} {Nom}".Trim();
    public bool TelephoneVerifie => Telephone is not null && TelephoneVerifieLe is not null;
    public bool ConsentementWhatsapp => ConsentementWhatsappLe is not null;

    /// <summary>Règle WhatsApp : numéro valide + numéro lié (vérifié) + consentement.</summary>
    public bool PeutRecevoirWhatsapp =>
        TelephoneVerifie && ConsentementWhatsapp && TelephoneE164Attribute.EstValide(Telephone);
}

/// <summary>Refresh token révocable (seul le hachage SHA-256 est stocké).</summary>
public class RefreshToken
{
    public int Id { get; set; }
    public string UtilisateurId { get; set; } = string.Empty;
    public Utilisateur Utilisateur { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreeLe { get; set; }
    public DateTime ExpireLe { get; set; }
    public DateTime? RevoqueLe { get; set; }
    public string? RemplaceParHash { get; set; }

    public bool EstActif(DateTime maintenant) => RevoqueLe is null && ExpireLe > maintenant;
}
