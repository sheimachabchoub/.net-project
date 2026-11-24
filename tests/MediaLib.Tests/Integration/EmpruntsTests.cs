using System.Net;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Shared;
using MediaLib.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaLib.Tests.Integration;

public class EmpruntsTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    private DateOnly Aujourdhui => DateOnly.FromDateTime(f.Horloge.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Emprunt_201_avec_echeance_a_21_jours()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(2);
        var r = await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        var e = await r.LireAsync<EmpruntDto>();
        Assert.Equal(Aujourdhui.AddDays(21), e.DateRetourPrevue);
        Assert.Equal(StatutEmprunt.EnCours, e.Statut);
        Assert.Equal(1, (await (await f.CreateClient().GetAsync($"api/livres/{livre.Id}")).LireAsync<LivreDto>()).NbDisponibles);
    }

    [Fact]
    public async Task Maximum_5_emprunts_en_cours()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        for (var i = 0; i < 5; i++)
        {
            var l = await f.NouveauLivreAsync();
            Assert.Equal(HttpStatusCode.Created, (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = l.Id })).StatusCode);
        }
        var sixieme = await f.NouveauLivreAsync();
        var r = await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = sixieme.Id });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Maximum", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pas_d_emprunt_sans_exemplaire_disponible()
    {
        var (a, _) = await f.NouveauMembreAsync();
        var (b, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(1);
        Assert.Equal(HttpStatusCode.Created, (await a.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = 987654 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).StatusCode);
    }

    [Fact]
    public async Task Mes_emprunts_ne_montrent_que_les_miens()
    {
        var (a, ua) = await f.NouveauMembreAsync();
        var (b, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(2);
        var e = await (await a.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();

        var miens = await (await a.GetAsync("api/emprunts/moi")).LireAsync<List<EmpruntDto>>();
        Assert.All(miens, x => Assert.Equal(ua.Id, x.UtilisateurId));
        Assert.Empty(await (await b.GetAsync("api/emprunts/moi")).LireAsync<List<EmpruntDto>>());
        // Un autre membre ne peut pas lire l'emprunt, ni le prolonger, ni lister le suivi.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"api/emprunts/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostJsonAsync($"api/emprunts/{e.Id}/prolongation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("api/emprunts?enRetard=true")).StatusCode);
        // Un membre ne peut pas emprunter pour quelqu'un d'autre : l'utilisateurId fourni est ignoré.
        var autre = await f.NouveauLivreAsync();
        var pourAutrui = await (await b.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = autre.Id, UtilisateurId = ua.Id })).LireAsync<EmpruntDto>();
        Assert.NotEqual(ua.Id, pourAutrui.UtilisateurId);
    }

    [Fact]
    public async Task Retour_par_le_bibliothecaire_puis_historique()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(1);
        var e = await (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await membre.PostJsonAsync($"api/emprunts/{e.Id}/retour")).StatusCode);
        var biblio = await f.BibliothecaireAsync();
        var rendu = await (await biblio.PostJsonAsync($"api/emprunts/{e.Id}/retour")).LireAsync<EmpruntDto>();
        Assert.Equal(StatutEmprunt.Rendu, rendu.Statut);
        Assert.Equal(HttpStatusCode.Conflict, (await biblio.PostJsonAsync($"api/emprunts/{e.Id}/retour")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biblio.PostJsonAsync("api/emprunts/999999/retour")).StatusCode);

        Assert.Empty(await (await membre.GetAsync("api/emprunts/moi")).LireAsync<List<EmpruntDto>>());
        Assert.Single(await (await membre.GetAsync("api/emprunts/moi/historique")).LireAsync<List<EmpruntDto>>());
        Assert.Equal(1, (await (await f.CreateClient().GetAsync($"api/livres/{livre.Id}")).LireAsync<LivreDto>()).NbDisponibles);
    }

    [Fact]
    public async Task Retards_visibles_par_le_bibliothecaire()
    {
        var (_, u) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync();
        var id = await f.AvecScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MediaLibDbContext>();
            var e = new Emprunt { LivreId = livre.Id, UtilisateurId = u.Id, DateEmprunt = Aujourdhui.AddDays(-30), DateRetourPrevue = Aujourdhui.AddDays(-9) };
            db.Emprunts.Add(e);
            (await db.Livres.FindAsync(livre.Id))!.NbDisponibles--;
            await db.SaveChangesAsync();
            return e.Id;
        });
        var retards = await (await (await f.BibliothecaireAsync()).GetAsync("api/emprunts?enRetard=true")).LireAsync<List<EmpruntDto>>();
        var retard = Assert.Single(retards, r => r.Id == id);
        Assert.Equal((StatutEmprunt.EnRetard, 9), (retard.Statut, retard.JoursRetard));
    }

    [Fact]
    public async Task Prolongation_validee_par_le_bibliothecaire()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync();
        var e = await (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();

        var demande = await membre.PostJsonAsync($"api/emprunts/{e.Id}/prolongation");
        Assert.Equal(HttpStatusCode.Accepted, demande.StatusCode);
        var decision = await demande.LireAsync<DecisionDto>();
        Assert.Equal((StatutDecision.EnAttenteValidation, Classement.ValidationRequise), (decision.Statut, decision.Classement));

        // L'échéance ne bouge pas tant que le bibliothécaire n'a pas validé.
        Assert.Equal(e.DateRetourPrevue, (await (await membre.GetAsync($"api/emprunts/{e.Id}")).LireAsync<EmpruntDto>()).DateRetourPrevue);
        Assert.Equal(HttpStatusCode.Forbidden, (await membre.PostJsonAsync($"api/decisions/{decision.Id}/approuver", new ValidationDecisionRequete())).StatusCode);

        var biblio = await f.BibliothecaireAsync();
        var approuvee = await (await biblio.PostJsonAsync($"api/decisions/{decision.Id}/approuver", new ValidationDecisionRequete { Commentaire = "OK" })).LireAsync<DecisionDto>();
        Assert.Equal(StatutDecision.Executee, approuvee.Statut);
        Assert.NotNull(approuvee.Validateur);
        var prolonge = await (await membre.GetAsync($"api/emprunts/{e.Id}")).LireAsync<EmpruntDto>();
        Assert.Equal(e.DateRetourPrevue.AddDays(7), prolonge.DateRetourPrevue);

        // Une seule prolongation ; et une décision déjà traitée ne se revalide pas.
        Assert.Equal(HttpStatusCode.Conflict, (await membre.PostJsonAsync($"api/emprunts/{e.Id}/prolongation")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await biblio.PostJsonAsync($"api/decisions/{decision.Id}/approuver", new ValidationDecisionRequete())).StatusCode);
    }

    [Fact]
    public async Task Prolongation_rejetee()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync();
        var e = await (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();
        var decision = await (await membre.PostJsonAsync($"api/emprunts/{e.Id}/prolongation")).LireAsync<DecisionDto>();
        var rejet = await (await (await f.BibliothecaireAsync()).PostJsonAsync($"api/decisions/{decision.Id}/rejeter", new ValidationDecisionRequete { Commentaire = "Réservé" })).LireAsync<DecisionDto>();
        Assert.Equal(StatutDecision.Rejetee, rejet.Statut);
        Assert.Equal(e.DateRetourPrevue, (await (await membre.GetAsync($"api/emprunts/{e.Id}")).LireAsync<EmpruntDto>()).DateRetourPrevue);
    }

    [Fact]
    public async Task Reservation_file_d_attente_et_mise_de_cote_au_retour()
    {
        var (a, _) = await f.NouveauMembreAsync("Alice");
        var (b, ub) = await f.NouveauMembreAsync("Bilel");
        var (c, _) = await f.NouveauMembreAsync("Chiraz");
        var livre = await f.NouveauLivreAsync(1);
        var emprunt = await (await a.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();

        Assert.Equal(HttpStatusCode.Conflict, (await a.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id })).StatusCode); // déjà en prêt
        var rb = await b.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id });
        Assert.Equal(HttpStatusCode.Created, rb.StatusCode);
        Assert.Equal(1, (await rb.LireAsync<ReservationDto>()).Position);
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id })).StatusCode); // doublon
        Assert.Equal(2, (await (await c.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id })).LireAsync<ReservationDto>()).Position);

        f.Boite.Vider();
        await (await f.BibliothecaireAsync()).PostJsonAsync($"api/emprunts/{emprunt.Id}/retour");

        // Bilel est prévenu et l'exemplaire lui est réservé : Chiraz ne peut pas l'emprunter.
        var resaB = Assert.Single(await (await b.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>());
        Assert.Equal(StatutReservation.Notifiee, resaB.Statut);
        Assert.NotNull(resaB.ExpireLe);
        Assert.Contains(f.Boite.Envoyes, m => m.Contenu.Contains(livre.Titre) && m.Contenu.Contains("Bilel"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).StatusCode);

        // Bilel l'emprunte : la réservation est honorée.
        Assert.Equal(HttpStatusCode.Created, (await b.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).StatusCode);
        Assert.Equal(StatutReservation.Honoree, Assert.Single(await (await b.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>()).Statut);
        Assert.Equal(1, Assert.Single(await (await c.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>()).Position);
        _ = ub;
    }

    [Fact]
    public async Task Reserver_un_livre_disponible_est_inutile_409()
    {
        var (m, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(3);
        Assert.Equal(HttpStatusCode.Conflict, (await m.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id })).StatusCode);
    }

    [Fact]
    public async Task Reservation_non_retiree_expire_et_passe_au_suivant()
    {
        var (a, _) = await f.NouveauMembreAsync();
        var (b, _) = await f.NouveauMembreAsync();
        var (c, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(1);
        var e = await (await a.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();
        await b.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id });
        await c.PostJsonAsync("api/reservations", new ReservationRequete { LivreId = livre.Id });
        var biblio = await f.BibliothecaireAsync();
        await biblio.PostJsonAsync($"api/emprunts/{e.Id}/retour");

        f.Horloge.Advance(TimeSpan.FromDays(5));
        try
        {
            var bilan = await (await biblio.PostJsonAsync("api/relances/traitement")).LireAsync<TraitementRelancesDto>();
            Assert.True(bilan.ReservationsExpirees >= 1);
            Assert.Equal(StatutReservation.Expiree, Assert.Single(await (await b.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>()).Statut);
            Assert.Equal(StatutReservation.Notifiee, Assert.Single(await (await c.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>()).Statut);
        }
        finally
        {
            f.Horloge.SetUtcNow(DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Le_dernier_exemplaire_ne_part_qu_une_fois_en_concurrence()
    {
        var livre = await f.NouveauLivreAsync(1);
        var membres = new List<HttpClient>();
        for (var i = 0; i < 4; i++) membres.Add((await f.NouveauMembreAsync()).Client);
        var reponses = await Task.WhenAll(membres.Select(m => m.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })));
        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.Created);
        var stock = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Livres.Where(l => l.Id == livre.Id).Select(l => l.NbDisponibles).FirstAsync());
        Assert.Equal(0, stock);
    }
}
