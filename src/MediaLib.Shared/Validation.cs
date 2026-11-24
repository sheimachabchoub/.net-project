using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MediaLib.Shared;

/// <summary>Validation et normalisation des ISBN-10 / ISBN-13 (clé de contrôle comprise).</summary>
public static class IsbnValidateur
{
    /// <summary>Retire tirets et espaces, met le X final en majuscule.</summary>
    public static string Normaliser(string? isbn) =>
        new string((isbn ?? string.Empty).Where(c => char.IsDigit(c) || c is 'x' or 'X').ToArray()).ToUpperInvariant();

    public static bool EstValide(string? isbn)
    {
        var s = Normaliser(isbn);
        return s.Length switch
        {
            10 => Isbn10(s),
            13 => Isbn13(s),
            _ => false,
        };
    }

    private static bool Isbn10(string s)
    {
        var somme = 0;
        for (var i = 0; i < 10; i++)
        {
            int valeur;
            if (s[i] == 'X')
            {
                if (i != 9) return false;
                valeur = 10;
            }
            else valeur = s[i] - '0';
            somme += valeur * (10 - i);
        }
        return somme % 11 == 0;
    }

    private static bool Isbn13(string s)
    {
        if (s.Contains('X')) return false;
        var somme = 0;
        for (var i = 0; i < 12; i++) somme += (s[i] - '0') * (i % 2 == 0 ? 1 : 3);
        var cle = (10 - somme % 10) % 10;
        return cle == s[12] - '0';
    }
}

/// <summary>Attribut DataAnnotations : ISBN-10 ou ISBN-13 valide.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IsbnAttribute : ValidationAttribute
{
    public IsbnAttribute() : base("L'ISBN doit être un ISBN-10 ou ISBN-13 valide (clé de contrôle comprise).") { }

    public override bool IsValid(object? value) => value is null || (value is string s && IsbnValidateur.EstValide(s));
}

/// <summary>Numéro de téléphone au format international E.164 (ex. +21650123456).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed partial class TelephoneE164Attribute : ValidationAttribute
{
    public TelephoneE164Attribute() : base("Le numéro doit être au format international, par exemple +21650123456.") { }

    public override bool IsValid(object? value) =>
        value is null || (value is string s && (s.Length == 0 || Regex().IsMatch(s)));

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex Regex();

    public static bool EstValide(string? numero) => numero is not null && Regex().IsMatch(numero);
}

/// <summary>Options JSON communes à l'API et au client Blazor (enums en texte).</summary>
public static class MediaLibJson
{
    public static readonly JsonSerializerOptions Options = Creer();

    public static JsonSerializerOptions Creer()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
