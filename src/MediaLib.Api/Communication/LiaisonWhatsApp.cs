using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MediaLib.Api.Configuration;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Communication;

/// <summary>
/// Liaison d'un numéro WhatsApp à un compte : l'adhérent donne son consentement, reçoit un code
/// à 6 chiffres et l'envoie depuis WhatsApp (« LIAISON 123456 ») : le numéro est alors vérifié.
/// </summary>
public partial class LiaisonWhatsApp(MediaLibDbContext db, IOptions<WhatsAppOptions> options, TimeProvider horloge)
{
    public const int DureeMinutes = 15;
    public const string MotCle = "LIAISON";

    public async Task DefinirConsentementAsync(Utilisateur u, bool consentement, CancellationToken ct = default)
    {
        u.ConsentementWhatsappLe = consentement ? horloge.Maintenant() : null;
        if (!consentement)
        {
            u.CodeLiaison = null;
            if (u.CanalPrefere == Canal.WhatsApp) u.CanalPrefere = Canal.Email;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<LiaisonWhatsappDto> DemarrerAsync(Utilisateur u, CancellationToken ct = default)
    {
        if (!u.ConsentementWhatsapp) throw new RegleMetierException("Donnez d'abord votre consentement aux messages WhatsApp.");
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        u.CodeLiaison = code;
        u.CodeLiaisonExpireLe = horloge.Maintenant().AddMinutes(DureeMinutes);
        await db.SaveChangesAsync(ct);
        var numero = new string(options.Value.NumeroPublic.Where(char.IsDigit).ToArray());
        return new LiaisonWhatsappDto(code, $"https://wa.me/{numero}?text={Uri.EscapeDataString($"{MotCle} {code}")}", u.CodeLiaisonExpireLe.Value);
    }

    public static string? ExtraireCode(string texte)
    {
        var m = CodeRegex().Match(texte);
        return m.Success ? m.Groups[1].Value : null;
    }

    public async Task<Utilisateur?> VerifierAsync(string code, string telephoneExpediteur, CancellationToken ct = default)
    {
        var maintenant = horloge.Maintenant();
        var u = await db.Users.FirstOrDefaultAsync(x => x.CodeLiaison == code, ct);
        if (u is null || u.CodeLiaisonExpireLe is null || u.CodeLiaisonExpireLe < maintenant) return null;
        var telephone = "+" + telephoneExpediteur.TrimStart('+');
        if (!TelephoneE164Attribute.EstValide(telephone)) return null;

        // Un numéro n'appartient qu'à un compte : l'ancien titulaire le perd.
        var ancien = await db.Users.FirstOrDefaultAsync(x => x.Telephone == telephone && x.Id != u.Id, ct);
        if (ancien is not null)
        {
            ancien.Telephone = null;
            ancien.TelephoneVerifieLe = null;
            await db.SaveChangesAsync(ct);
        }
        u.Telephone = telephone;
        u.TelephoneVerifieLe = maintenant;
        u.CodeLiaison = null;
        u.CodeLiaisonExpireLe = null;
        await db.SaveChangesAsync(ct);
        return u;
    }

    public async Task RevoquerConsentementAsync(Utilisateur u, CancellationToken ct = default) =>
        await DefinirConsentementAsync(u, false, ct);

    public async Task DelierAsync(Utilisateur u, CancellationToken ct = default)
    {
        u.Telephone = null;
        u.TelephoneVerifieLe = null;
        u.CodeLiaison = null;
        if (u.CanalPrefere == Canal.WhatsApp) u.CanalPrefere = Canal.Email;
        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"^\s*LIAISON\s+(\d{6})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();
}
