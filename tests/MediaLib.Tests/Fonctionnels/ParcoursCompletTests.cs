using System.Net;
using MediaLib.Shared;
using MediaLib.Tests.Infrastructure;

namespace MediaLib.Tests.Fonctionnels;

/// <summary>Parcours de bout en bout, uniquement via l'API HTTP, comme le ferait l'application Blazor.</summary>
public class ParcoursCompletTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    [Fact]
    public async Task Inscription_recherche_emprunt_retour_historique()
    {
        var anonyme = f.CreateClient();
        var email = $"parcours-{Guid.NewGuid():N}@test.local";
        Assert.Equal(HttpStatusCode.Created, (await anonyme.PostJsonAsync("api/auth/inscription",
            new InscriptionRequete { Email = email, Prenom = "Nour", Nom = "Parcours", MotDePasse = "Parcours1", ConfirmationMotDePasse = "Parcours1" })).StatusCode);
        var membre = f.Client((await f.ConnecterAsync(email, "Parcours1")).AccessToken);

        var livre = (await (await anonyme.GetAsync("api/livres?q=Candide")).LireAsync<PageResultat<LivreDto>>()).Elements.Single();
        var emprunt = await (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();
        Assert.Equal(livre.NbDisponibles - 1, (await (await anonyme.GetAsync($"api/livres/{livre.Id}")).LireAsync<LivreDto>()).NbDisponibles);

        var assistant = await (await membre.PostJsonAsync("api/agent/messages", new QuestionAgentRequete { Texte = "mes emprunts" })).LireAsync<ReponseAgentDto>();
        Assert.Contains("Candide", assistant.Texte);

        var biblio = await f.BibliothecaireAsync();
        Assert.Contains(await (await biblio.GetAsync("api/emprunts")).LireAsync<List<EmpruntDto>>(), e => e.Id == emprunt.Id);
        Assert.Equal(HttpStatusCode.OK, (await biblio.PostJsonAsync($"api/emprunts/{emprunt.Id}/retour")).StatusCode);

        var historique = await (await membre.GetAsync("api/emprunts/moi/historique")).LireAsync<List<EmpruntDto>>();
        Assert.Equal("Candide", Assert.Single(historique).LivreTitre);
        Assert.Equal(livre.NbDisponibles, (await (await anonyme.GetAsync($"api/livres/{livre.Id}")).LireAsync<LivreDto>()).NbDisponibles);
    }

    [Fact]
    public async Task Bibliothecaire_gere_le_catalogue_et_l_admin_les_roles()
    {
        var biblio = await f.BibliothecaireAsync();
        var auteur = await (await biblio.PostJsonAsync("api/auteurs", new AuteurRequete { Prenom = "Tahar", Nom = "Ben Jelloun" })).LireAsync<AuteurDto>();
        var categorie = await (await biblio.PostJsonAsync("api/categories", new CategorieRequete { Nom = "Maghreb" })).LireAsync<CategorieDto>();
        var livre = await (await biblio.PostJsonAsync("api/livres", new LivreRequete
        {
            Titre = "L'Enfant de sable", Isbn = "2020123452", Annee = 1985, NbExemplaires = 1, AuteurId = auteur.Id, CategorieId = categorie.Id,
        })).LireAsync<LivreDto>();
        Assert.Equal("Tahar Ben Jelloun", livre.AuteurNom);
        Assert.Equal(HttpStatusCode.Conflict, (await biblio.DeleteAsync($"api/auteurs/{auteur.Id}")).StatusCode);

        var (_, u) = await f.NouveauMembreAsync();
        var admin = await f.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutJsonAsync($"api/admin/utilisateurs/{u.Id}/roles", new RolesRequete { Roles = [Roles.Bibliothecaire] })).StatusCode);
    }
}
