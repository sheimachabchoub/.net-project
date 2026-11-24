using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Connaissance;

/// <summary>Normalisation du français : minuscules, sans accents, mots vides retirés, racinisation légère.</summary>
public static partial class Normaliseur
{
    private static readonly HashSet<string> MotsVides =
    [
        "a", "ai", "au", "aux", "avec", "ce", "ces", "cet", "cette", "dans", "de", "des", "du", "elle", "en", "et", "est",
        "il", "ils", "je", "j", "la", "le", "les", "leur", "lui", "ma", "mais", "me", "mes", "moi", "mon", "ne", "nos", "notre",
        "nous", "on", "ou", "par", "pas", "pour", "qu", "que", "qui", "sa", "se", "ses", "son", "sur", "ta", "te", "tes", "toi",
        "ton", "tu", "un", "une", "vos", "votre", "vous", "y", "d", "l", "n", "s", "t", "c", "m", "ca", "cela",
        "suis", "es", "sont", "etre", "avez", "avons", "ont", "peux", "peut", "puis", "quel", "quelle", "quels",
        "quelles", "combien", "comment", "quand", "pourquoi", "si", "plus", "tres", "bien", "faire", "fait", "dois",
        "doit", "bonjour", "merci", "svp", "stp", "quoi", "ceci", "tout", "tous", "toute", "toutes", "est-ce",
    ];

    private static readonly string[] Suffixes =
        ["ements", "ement", "ations", "ation", "ables", "able", "euses", "euse", "eurs", "eur", "ees", "ee", "er", "ez", "es", "s", "x", "e"];

    /// <summary>Expansion de requête : une question courante vers le vocabulaire du règlement.</summary>
    private static readonly Dictionary<string, string[]> Synonymes = new()
    {
        ["combien"] = ["nombre", "maximum"],
        ["longtemps"] = ["duree"],
        ["garder"] = ["duree"],
        ["ouvert"] = ["horaires", "ouverture"],
        ["ferme"] = ["horaires", "fermee"],
        ["amende"] = ["retard"],
        ["penalite"] = ["retard"],
        ["rallonger"] = ["prolongation"],
    };

    public static string SansAccents(string texte)
    {
        var decompose = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Replace('’', ' ').Replace('\'', ' ').Replace("œ", "oe").Replace("æ", "ae");
    }

    private static IEnumerable<string> Mots(string texte) =>
        MotRegex().Split(SansAccents(texte)).Where(m => m.Length > 0);

    /// <summary>Termes indexables (racinisés) d'un texte.</summary>
    public static List<string> Termes(string texte) =>
        Mots(texte).Where(m => !MotsVides.Contains(m) && (m.Length >= 2 || m.All(char.IsDigit))).Select(Raciner).ToList();

    /// <summary>Termes d'une question, enrichis des synonymes du domaine.</summary>
    public static List<string> TermesRequete(string question)
    {
        var termes = Termes(question);
        foreach (var mot in Mots(question))
            if (Synonymes.TryGetValue(mot, out var ajouts)) termes.AddRange(ajouts.Select(Raciner));
        return termes;
    }

    /// <summary>Mots significatifs (non racinisés, > 2 lettres) : utiles pour retrouver un titre.</summary>
    public static List<string> MotsSignificatifs(string texte) =>
        Mots(texte).Where(m => m.Length > 2 && !MotsVides.Contains(m)).Distinct().ToList();

    public static string Raciner(string mot)
    {
        if (mot.Length <= 4 || mot.All(char.IsDigit)) return mot;
        foreach (var s in Suffixes)
            if (mot.EndsWith(s, StringComparison.Ordinal) && mot.Length - s.Length >= 4) return mot[..^s.Length];
        return mot;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex MotRegex();
}

/// <summary>Découpe les documents publiés en fragments (section, rang) et maintient l'index.</summary>
public partial class IndexeurConnaissance(MediaLibDbContext db, TimeProvider horloge)
{
    private const int Chevauchement = 160;

    public async Task<int> IndexerAsync(DocumentConnaissance document, CancellationToken ct = default)
    {
        await db.Fragments.Where(f => f.DocumentId == document.Id).ExecuteDeleteAsync(ct);
        if (document.Statut != StatutPublication.Publie)
        {
            document.IndexeLe = null;
            await db.SaveChangesAsync(ct);
            return 0;
        }
        var rang = 0;
        var precedent = string.Empty;
        // Le titre du document pèse plus lourd : « horaires » doit d'abord trouver le document « Horaires ».
        var titre = string.Concat(Enumerable.Repeat(document.Titre + " ", 3));
        foreach (var (section, contenu) in Decouper(document.Contenu))
        {
            var recouvrement = precedent.Length > Chevauchement ? precedent[^Chevauchement..] : precedent;
            var termes = Normaliseur.Termes($"{titre} {section} {contenu} {recouvrement}");
            db.Fragments.Add(new FragmentConnaissance
            {
                DocumentId = document.Id, Section = section, Contenu = contenu, Termes = string.Join(' ', termes), Rang = rang++,
            });
            precedent = contenu;
        }
        document.IndexeLe = horloge.Maintenant();
        await db.SaveChangesAsync(ct);
        return rang;
    }

    public async Task<int> IndexerToutAsync(CancellationToken ct = default)
    {
        var n = 0;
        foreach (var d in await db.Documents.ToListAsync(ct))
        {
            await IndexerAsync(d, ct);
            if (d.Statut == StatutPublication.Publie) n++;
        }
        return n;
    }

    /// <summary>Publie un document : les autres versions publiées du même titre sont archivées (et désindexées).</summary>
    public async Task PublierAsync(DocumentConnaissance document, CancellationToken ct = default)
    {
        var autres = await db.Documents.Where(d => d.Titre == document.Titre && d.Statut == StatutPublication.Publie && d.Id != document.Id).ToListAsync(ct);
        foreach (var autre in autres)
        {
            autre.Statut = StatutPublication.Archive;
            await IndexerAsync(autre, ct);
        }
        document.Statut = StatutPublication.Publie;
        document.PublieLe = horloge.Maintenant();
        await db.SaveChangesAsync(ct);
        await IndexerAsync(document, ct);
    }

    public async Task ArchiverAsync(DocumentConnaissance document, CancellationToken ct = default)
    {
        document.Statut = StatutPublication.Archive;
        await db.SaveChangesAsync(ct);
        await IndexerAsync(document, ct);
    }

    /// <summary>Découpage Markdown par titres (#, ##, ###). Le titre de niveau 1 est celui du document.</summary>
    public static List<(string Section, string Contenu)> Decouper(string markdown)
    {
        var sections = new List<(string, string)>();
        var titre = "Introduction";
        var lignes = new List<string>();
        var chemin = new List<string>();

        void Vider()
        {
            var contenu = string.Join('\n', lignes).Trim();
            if (contenu.Length > 0) sections.Add((titre, contenu));
            lignes.Clear();
        }

        foreach (var ligne in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var m = TitreRegex().Match(ligne);
            if (m.Success)
            {
                Vider();
                var niveau = m.Groups[1].Value.Length;
                while (chemin.Count >= niveau) chemin.RemoveAt(chemin.Count - 1);
                while (chemin.Count < niveau - 1) chemin.Add(string.Empty);
                chemin.Add(m.Groups[2].Value.Trim());
                var utiles = chemin.Where(c => c.Length > 0).ToList();
                titre = string.Join(" › ", utiles.Count > 1 ? utiles.Skip(1) : utiles);
                continue;
            }
            lignes.Add(ligne);
        }
        Vider();
        return sections;
    }

    [GeneratedRegex(@"^(#{1,3})\s+(.+?)\s*#*$")]
    private static partial Regex TitreRegex();
}

/// <summary>Recherche BM25 sur les fragments des seuls documents publiés (brouillons et archives exclus).</summary>
public class RechercheConnaissance(MediaLibDbContext db)
{
    private const double K1 = 1.4;
    private const double B = 0.75;

    public async Task<List<ExtraitDto>> RechercherAsync(string question, int limite = 3, CancellationToken ct = default)
    {
        var requete = Normaliseur.TermesRequete(question).Distinct().ToList();
        if (requete.Count == 0) return [];
        var fragments = await db.Fragments.AsNoTracking().Include(f => f.Document)
            .Where(f => f.Document.Statut == StatutPublication.Publie).ToListAsync(ct);
        if (fragments.Count == 0) return [];

        var frequences = fragments.Select(f => f.Termes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count())).ToList();
        var longueurs = frequences.Select(f => f.Values.Sum()).ToList();
        var n = fragments.Count;
        var longueurMoyenne = Math.Max(1.0, longueurs.Average());
        var df = new Dictionary<string, int>();
        foreach (var f in frequences)
            foreach (var t in f.Keys) df[t] = df.GetValueOrDefault(t) + 1;

        var scores = new List<(int Index, double Score)>();
        for (var i = 0; i < n; i++)
        {
            double score = 0;
            foreach (var terme in requete)
            {
                if (!frequences[i].TryGetValue(terme, out var tf)) continue;
                var idf = Math.Log(1 + (n - df[terme] + 0.5) / (df[terme] + 0.5));
                score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * longueurs[i] / longueurMoyenne));
            }
            if (score > 0) scores.Add((i, score));
        }

        return scores.OrderByDescending(s => s.Score).Take(limite).Select(s =>
        {
            var f = fragments[s.Index];
            return new ExtraitDto(f.DocumentId, f.Document.Titre, f.Document.Version, f.Section, f.Contenu, Math.Round(s.Score, 3));
        }).ToList();
    }
}
