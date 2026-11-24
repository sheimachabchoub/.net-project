using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaLib.Api.Controllers;

/// <summary>Livres : catalogue public (recherche, filtre, tri, pagination) et gestion réservée aux bibliothécaires.</summary>
[ApiController]
[Route("api/livres")]
[Produces("application/json")]
public class LivresController(CatalogueService catalogue) : ControllerBase
{
    /// <summary>Recherche par titre / auteur / ISBN, filtre par catégorie, tri et pagination.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PageResultat<LivreDto>>(StatusCodes.Status200OK)]
    public async Task<PageResultat<LivreDto>> Rechercher(
        [FromQuery] string? q, [FromQuery] int? categorieId, [FromQuery] TriLivres tri = TriLivres.Titre,
        [FromQuery] int page = 1, [FromQuery] int taillePage = 10, [FromQuery] bool disponibles = false, CancellationToken ct = default) =>
        await catalogue.RechercherAsync(q, categorieId, tri, page, taillePage, disponibles, ct);

    /// <summary>Détail d'un livre.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType<LivreDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<LivreDto> Detail(int id, CancellationToken ct) => await catalogue.LivreAsync(id, ct);

    /// <summary>Ajoute un livre (ISBN-10/13 valide et unique).</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<LivreDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LivreDto>> Creer(LivreRequete requete, CancellationToken ct)
    {
        var livre = await catalogue.CreerLivreAsync(requete, ct);
        return CreatedAtAction(nameof(Detail), new { id = livre.Id }, livre);
    }

    /// <summary>Modifie un livre.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<LivreDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<LivreDto> Modifier(int id, LivreRequete requete, CancellationToken ct) => await catalogue.ModifierLivreAsync(id, requete, ct);

    /// <summary>Supprime un livre sans historique d'emprunt.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        await catalogue.SupprimerLivreAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Auteurs.</summary>
[ApiController]
[Route("api/auteurs")]
[Produces("application/json")]
public class AuteursController(CatalogueService catalogue) : ControllerBase
{
    /// <summary>Liste des auteurs.</summary>
    [HttpGet, AllowAnonymous]
    [ProducesResponseType<List<AuteurDto>>(StatusCodes.Status200OK)]
    public async Task<List<AuteurDto>> Lister(CancellationToken ct) => await catalogue.AuteursAsync(ct);

    /// <summary>Détail d'un auteur.</summary>
    [HttpGet("{id:int}"), AllowAnonymous]
    [ProducesResponseType<AuteurDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AuteurDto> Detail(int id, CancellationToken ct) => await catalogue.AuteurAsync(id, ct);

    /// <summary>Crée un auteur.</summary>
    [HttpPost, Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<AuteurDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuteurDto>> Creer(AuteurRequete requete, CancellationToken ct)
    {
        var a = await catalogue.CreerAuteurAsync(requete, ct);
        return CreatedAtAction(nameof(Detail), new { id = a.Id }, a);
    }

    /// <summary>Modifie un auteur.</summary>
    [HttpPut("{id:int}"), Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<AuteurDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AuteurDto> Modifier(int id, AuteurRequete requete, CancellationToken ct) => await catalogue.ModifierAuteurAsync(id, requete, ct);

    /// <summary>Supprime un auteur (409 s'il est lié à un livre).</summary>
    [HttpDelete("{id:int}"), Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        await catalogue.SupprimerAuteurAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Catégories.</summary>
[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController(CatalogueService catalogue) : ControllerBase
{
    /// <summary>Liste des catégories.</summary>
    [HttpGet, AllowAnonymous]
    [ProducesResponseType<List<CategorieDto>>(StatusCodes.Status200OK)]
    public async Task<List<CategorieDto>> Lister(CancellationToken ct) => await catalogue.CategoriesAsync(ct);

    /// <summary>Détail d'une catégorie.</summary>
    [HttpGet("{id:int}"), AllowAnonymous]
    [ProducesResponseType<CategorieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CategorieDto> Detail(int id, CancellationToken ct) => await catalogue.CategorieAsync(id, ct);

    /// <summary>Crée une catégorie (nom unique).</summary>
    [HttpPost, Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<CategorieDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategorieDto>> Creer(CategorieRequete requete, CancellationToken ct)
    {
        var c = await catalogue.CreerCategorieAsync(requete, ct);
        return CreatedAtAction(nameof(Detail), new { id = c.Id }, c);
    }

    /// <summary>Modifie une catégorie.</summary>
    [HttpPut("{id:int}"), Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType<CategorieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CategorieDto> Modifier(int id, CategorieRequete requete, CancellationToken ct) => await catalogue.ModifierCategorieAsync(id, requete, ct);

    /// <summary>Supprime une catégorie (409 si elle est liée à un livre).</summary>
    [HttpDelete("{id:int}"), Authorize(Roles = Roles.Gestion)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        await catalogue.SupprimerCategorieAsync(id, ct);
        return NoContent();
    }
}
