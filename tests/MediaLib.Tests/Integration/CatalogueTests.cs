using System.Net;
using MediaLib.Api.Data;
using MediaLib.Shared;
using MediaLib.Tests.Infrastructure;

namespace MediaLib.Tests.Integration;

public class CatalogueTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    [Fact]
    public async Task Seed_10_auteurs_5_categories_30_livres_et_3_comptes()
    {
        var client = f.CreateClient();
        Assert.True((await (await client.GetAsync("api/auteurs")).LireAsync<List<AuteurDto>>()).Count >= 10);
        Assert.Equal(5, (await (await client.GetAsync("api/categories")).LireAsync<List<CategorieDto>>()).Count(c => c.NbLivres > 0));
        var page = await (await client.GetAsync("api/livres?taillePage=50")).LireAsync<PageResultat<LivreDto>>();
        Assert.True(page.Total >= 30);
        Assert.All(page.Elements, l => Assert.True(IsbnValidateur.EstValide(l.Isbn)));
        foreach (var (email, mdp) in new[] { (ComptesDemo.MembreEmail, ComptesDemo.MembreMotDePasse), (ComptesDemo.BibliothecaireEmail, ComptesDemo.BibliothecaireMotDePasse), (ComptesDemo.AdminEmail, ComptesDemo.AdminMotDePasse) })
            Assert.NotNull(await f.ConnecterAsync(email, mdp));
    }

    [Fact]
    public async Task Recherche_par_titre_et_par_auteur()
    {
        var parTitre = await (await f.CreateClient().GetAsync("api/livres?q=Germinal")).LireAsync<PageResultat<LivreDto>>();
        Assert.Equal("Germinal", Assert.Single(parTitre.Elements).Titre);
        var parAuteur = await (await f.CreateClient().GetAsync("api/livres?q=Jules%20Verne&taillePage=20")).LireAsync<PageResultat<LivreDto>>();
        Assert.Equal(4, parAuteur.Total);
        Assert.All(parAuteur.Elements, l => Assert.Equal("Jules Verne", l.AuteurNom));
    }

    [Fact]
    public async Task Filtre_categorie_et_tri()
    {
        var categories = await (await f.CreateClient().GetAsync("api/categories")).LireAsync<List<CategorieDto>>();
        var policier = categories.Single(c => c.Nom == "Policier");
        var page = await (await f.CreateClient().GetAsync($"api/livres?categorieId={policier.Id}&tri=AnneeDesc&taillePage=50")).LireAsync<PageResultat<LivreDto>>();
        Assert.All(page.Elements, l => Assert.Equal("Policier", l.CategorieNom));
        Assert.Equal(page.Elements.Select(l => l.Annee).OrderByDescending(a => a), page.Elements.Select(l => l.Annee));
    }

    [Fact]
    public async Task Pagination()
    {
        var p1 = await (await f.CreateClient().GetAsync("api/livres?q=e&taillePage=5&page=1")).LireAsync<PageResultat<LivreDto>>();
        var p2 = await (await f.CreateClient().GetAsync("api/livres?q=e&taillePage=5&page=2")).LireAsync<PageResultat<LivreDto>>();
        Assert.Equal(5, p1.Elements.Count);
        Assert.True(p1.NbPages >= 2);
        Assert.Empty(p1.Elements.Select(l => l.Id).Intersect(p2.Elements.Select(l => l.Id)));
        var grande = await (await f.CreateClient().GetAsync("api/livres?taillePage=1000")).LireAsync<PageResultat<LivreDto>>();
        Assert.Equal(50, grande.TaillePage); // plafonnée
    }

    [Fact]
    public async Task Detail_et_404()
    {
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("api/livres/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync("api/livres/999999")).StatusCode);
    }

    [Fact]
    public async Task Crud_livre_par_le_bibliothecaire()
    {
        var biblio = await f.BibliothecaireAsync();
        var auteurs = await (await biblio.GetAsync("api/auteurs")).LireAsync<List<AuteurDto>>();
        var categories = await (await biblio.GetAsync("api/categories")).LireAsync<List<CategorieDto>>();
        var requete = new LivreRequete { Titre = "Le Livre du test CRUD", Isbn = "978-0-306-40615-7", Annee = 2001, NbExemplaires = 2, AuteurId = auteurs[0].Id, CategorieId = categories[0].Id };

        var cree = await biblio.PostJsonAsync("api/livres", requete);
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        Assert.NotNull(cree.Headers.Location);
        var livre = await cree.LireAsync<LivreDto>();
        Assert.Equal("9780306406157", livre.Isbn);
        Assert.Equal(2, livre.NbDisponibles);

        Assert.Equal(HttpStatusCode.Conflict, (await biblio.PostJsonAsync("api/livres", requete)).StatusCode); // ISBN unique

        requete.Titre = "Titre modifié";
        requete.NbExemplaires = 3;
        var modifie = await (await biblio.PutJsonAsync($"api/livres/{livre.Id}", requete)).LireAsync<LivreDto>();
        Assert.Equal(("Titre modifié", 3), (modifie.Titre, modifie.NbDisponibles));

        Assert.Equal(HttpStatusCode.NoContent, (await biblio.DeleteAsync($"api/livres/{livre.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biblio.GetAsync($"api/livres/{livre.Id}")).StatusCode);
    }

    [Fact]
    public async Task Isbn_invalide_400()
    {
        var biblio = await f.BibliothecaireAsync();
        var r = await biblio.PostJsonAsync("api/livres", new LivreRequete { Titre = "X", Isbn = "9780306406158", Annee = 2000, NbExemplaires = 1, AuteurId = 1, CategorieId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("ISBN", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ecriture_interdite_au_membre_403_et_a_l_anonyme_401()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var requete = new CategorieRequete { Nom = "Interdite" };
        Assert.Equal(HttpStatusCode.Forbidden, (await membre.PostJsonAsync("api/categories", requete)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/categories", requete)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await membre.DeleteAsync("api/livres/1")).StatusCode);
    }

    [Fact]
    public async Task Suppression_auteur_ou_categorie_lies_409_sinon_204()
    {
        var biblio = await f.BibliothecaireAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await biblio.DeleteAsync("api/auteurs/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await biblio.DeleteAsync("api/categories/1")).StatusCode);

        var auteur = await (await biblio.PostJsonAsync("api/auteurs", new AuteurRequete { Prenom = "Libre", Nom = "Sans Livre" })).LireAsync<AuteurDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await biblio.DeleteAsync($"api/auteurs/{auteur.Id}")).StatusCode);
        var cat = await biblio.PostJsonAsync("api/categories", new CategorieRequete { Nom = $"Vide {Guid.NewGuid():N}"[..12] });
        Assert.Equal(HttpStatusCode.Created, cat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await biblio.DeleteAsync($"api/categories/{(await cat.LireAsync<CategorieDto>()).Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biblio.DeleteAsync("api/auteurs/99999")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_documente_les_endpoints()
    {
        var json = await f.CreateClient().GetStringAsync("openapi/v1.json");
        foreach (var chemin in new[] { "/api/auth/inscription", "/api/auth/connexion", "/api/auth/refresh", "/api/auth/deconnexion", "/api/auth/moi",
                     "/api/livres", "/api/livres/{id}", "/api/auteurs", "/api/categories", "/api/emprunts", "/api/emprunts/moi", "/api/emprunts/{id}/retour",
                     "/api/agent/messages", "/api/webhooks/whatsapp", "/api/decisions/{id}/approuver" })
            Assert.Contains($"\"{chemin}\"", json);
        Assert.Contains("\"bearer\"", json);
    }
}
