using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Services;

/// <summary>Catalogue : recherche, tri, pagination et CRUD livres / auteurs / catégories.</summary>
public class CatalogueService(MediaLibDbContext db)
{
    public const int TailleMax = 50;

    public async Task<PageResultat<LivreDto>> RechercherAsync(
        string? q, int? categorieId, TriLivres tri, int page, int taillePage, bool disponiblesSeulement = false, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        taillePage = Math.Clamp(taillePage, 1, TailleMax);
        var requete = db.Livres.AsNoTracking().Include(l => l.Auteur).Include(l => l.Categorie).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var motif = $"%{q.Trim()}%";
            var isbn = IsbnValidateur.Normaliser(q);
            requete = requete.Where(l =>
                EF.Functions.Like(l.Titre, motif)
                || EF.Functions.Like(l.Auteur.Nom, motif)
                || EF.Functions.Like(l.Auteur.Prenom, motif)
                || EF.Functions.Like(l.Auteur.Prenom + " " + l.Auteur.Nom, motif)
                || (isbn.Length >= 10 && l.Isbn == isbn));
        }
        if (categorieId is > 0) requete = requete.Where(l => l.CategorieId == categorieId);
        if (disponiblesSeulement) requete = requete.Where(l => l.NbDisponibles > 0);

        requete = tri switch
        {
            TriLivres.TitreDesc => requete.OrderByDescending(l => l.Titre),
            TriLivres.Annee => requete.OrderBy(l => l.Annee).ThenBy(l => l.Titre),
            TriLivres.AnneeDesc => requete.OrderByDescending(l => l.Annee).ThenBy(l => l.Titre),
            TriLivres.Auteur => requete.OrderBy(l => l.Auteur.Nom).ThenBy(l => l.Titre),
            TriLivres.Disponibles => requete.OrderByDescending(l => l.NbDisponibles).ThenBy(l => l.Titre),
            _ => requete.OrderBy(l => l.Titre),
        };

        var total = await requete.CountAsync(ct);
        var elements = await requete.Skip((page - 1) * taillePage).Take(taillePage).ToListAsync(ct);
        return new PageResultat<LivreDto>(elements.Select(Mapping.ToDto).ToList(), page, taillePage, total);
    }

    public async Task<LivreDto> LivreAsync(int id, CancellationToken ct = default)
    {
        var livre = await db.Livres.AsNoTracking().Include(l => l.Auteur).Include(l => l.Categorie)
            .FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new IntrouvableException($"Livre {id} introuvable.");
        return livre.ToDto();
    }

    public async Task<LivreDto> CreerLivreAsync(LivreRequete r, CancellationToken ct = default)
    {
        var livre = new Livre();
        await AppliquerAsync(livre, r, ct);
        livre.NbDisponibles = r.NbExemplaires;
        db.Livres.Add(livre);
        await db.SaveChangesAsync(ct);
        return await LivreAsync(livre.Id, ct);
    }

    public async Task<LivreDto> ModifierLivreAsync(int id, LivreRequete r, CancellationToken ct = default)
    {
        var livre = await db.Livres.FindAsync([id], ct) ?? throw new IntrouvableException($"Livre {id} introuvable.");
        var empruntes = livre.NbExemplaires - livre.NbDisponibles;
        if (r.NbExemplaires < empruntes)
            throw new RegleMetierException($"{empruntes} exemplaire(s) sont en prêt : impossible d'en déclarer seulement {r.NbExemplaires}.");
        await AppliquerAsync(livre, r, ct);
        livre.NbDisponibles = r.NbExemplaires - empruntes;
        livre.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return await LivreAsync(id, ct);
    }

    public async Task SupprimerLivreAsync(int id, CancellationToken ct = default)
    {
        var livre = await db.Livres.FindAsync([id], ct) ?? throw new IntrouvableException($"Livre {id} introuvable.");
        if (await db.Emprunts.AnyAsync(e => e.LivreId == id, ct))
            throw new RegleMetierException("Ce livre a un historique d'emprunts : il ne peut pas être supprimé.");
        db.Livres.Remove(livre);
        await db.SaveChangesAsync(ct);
    }

    private async Task AppliquerAsync(Livre livre, LivreRequete r, CancellationToken ct)
    {
        var isbn = IsbnValidateur.Normaliser(r.Isbn);
        if (!IsbnValidateur.EstValide(isbn)) throw new ArgumentException("ISBN invalide.");
        if (await db.Livres.AnyAsync(l => l.Isbn == isbn && l.Id != livre.Id, ct))
            throw new RegleMetierException($"L'ISBN {isbn} existe déjà dans le catalogue.");
        if (!await db.Auteurs.AnyAsync(a => a.Id == r.AuteurId, ct)) throw new ArgumentException("Auteur inconnu.");
        if (!await db.Categories.AnyAsync(c => c.Id == r.CategorieId, ct)) throw new ArgumentException("Catégorie inconnue.");
        livre.Titre = r.Titre.Trim();
        livre.Isbn = isbn;
        livre.Resume = r.Resume;
        livre.Annee = r.Annee;
        livre.NbExemplaires = r.NbExemplaires;
        livre.Couverture = string.IsNullOrWhiteSpace(r.Couverture) ? null : r.Couverture;
        livre.AuteurId = r.AuteurId;
        livre.CategorieId = r.CategorieId;
    }

    // ------------------------------------------------------------ Auteurs

    public async Task<List<AuteurDto>> AuteursAsync(CancellationToken ct = default) =>
        await db.Auteurs.AsNoTracking().OrderBy(a => a.Nom).ThenBy(a => a.Prenom)
            .Select(a => new AuteurDto(a.Id, a.Prenom, a.Nom, a.Biographie, a.Livres.Count)).ToListAsync(ct);

    public async Task<AuteurDto> AuteurAsync(int id, CancellationToken ct = default) =>
        await db.Auteurs.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new AuteurDto(a.Id, a.Prenom, a.Nom, a.Biographie, a.Livres.Count)).FirstOrDefaultAsync(ct)
        ?? throw new IntrouvableException($"Auteur {id} introuvable.");

    public async Task<AuteurDto> CreerAuteurAsync(AuteurRequete r, CancellationToken ct = default)
    {
        var a = new Auteur { Prenom = r.Prenom.Trim(), Nom = r.Nom.Trim(), Biographie = r.Biographie };
        db.Auteurs.Add(a);
        await db.SaveChangesAsync(ct);
        return await AuteurAsync(a.Id, ct);
    }

    public async Task<AuteurDto> ModifierAuteurAsync(int id, AuteurRequete r, CancellationToken ct = default)
    {
        var a = await db.Auteurs.FindAsync([id], ct) ?? throw new IntrouvableException($"Auteur {id} introuvable.");
        a.Prenom = r.Prenom.Trim();
        a.Nom = r.Nom.Trim();
        a.Biographie = r.Biographie;
        await db.SaveChangesAsync(ct);
        return await AuteurAsync(id, ct);
    }

    public async Task SupprimerAuteurAsync(int id, CancellationToken ct = default)
    {
        var a = await db.Auteurs.FindAsync([id], ct) ?? throw new IntrouvableException($"Auteur {id} introuvable.");
        if (await db.Livres.AnyAsync(l => l.AuteurId == id, ct))
            throw new RegleMetierException("Cet auteur est lié à au moins un livre : suppression interdite.");
        db.Auteurs.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ Catégories

    public async Task<List<CategorieDto>> CategoriesAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.Nom)
            .Select(c => new CategorieDto(c.Id, c.Nom, c.Description, c.Livres.Count)).ToListAsync(ct);

    public async Task<CategorieDto> CategorieAsync(int id, CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CategorieDto(c.Id, c.Nom, c.Description, c.Livres.Count)).FirstOrDefaultAsync(ct)
        ?? throw new IntrouvableException($"Catégorie {id} introuvable.");

    public async Task<CategorieDto> CreerCategorieAsync(CategorieRequete r, CancellationToken ct = default)
    {
        var nom = r.Nom.Trim();
        if (await db.Categories.AnyAsync(c => c.Nom == nom, ct)) throw new RegleMetierException($"La catégorie « {nom} » existe déjà.");
        var c = new Categorie { Nom = nom, Description = r.Description };
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return await CategorieAsync(c.Id, ct);
    }

    public async Task<CategorieDto> ModifierCategorieAsync(int id, CategorieRequete r, CancellationToken ct = default)
    {
        var c = await db.Categories.FindAsync([id], ct) ?? throw new IntrouvableException($"Catégorie {id} introuvable.");
        var nom = r.Nom.Trim();
        if (await db.Categories.AnyAsync(x => x.Nom == nom && x.Id != id, ct)) throw new RegleMetierException($"La catégorie « {nom} » existe déjà.");
        c.Nom = nom;
        c.Description = r.Description;
        await db.SaveChangesAsync(ct);
        return await CategorieAsync(id, ct);
    }

    public async Task SupprimerCategorieAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Categories.FindAsync([id], ct) ?? throw new IntrouvableException($"Catégorie {id} introuvable.");
        if (await db.Livres.AnyAsync(l => l.CategorieId == id, ct))
            throw new RegleMetierException("Cette catégorie est liée à au moins un livre : suppression interdite.");
        db.Categories.Remove(c);
        await db.SaveChangesAsync(ct);
    }
}
