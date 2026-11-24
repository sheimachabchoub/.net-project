using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaLib.Api.Agent;
using MediaLib.Api.Communication;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using MediaLib.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaLib.Tests.Integration;

public class RagTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    [Fact]
    public async Task Recherche_cite_titre_version_section()
    {
        var extraits = await (await f.CreateClient().GetAsync("api/documents/recherche?q=combien de temps puis-je garder un livre")).LireAsync<List<ExtraitDto>>();
        var premier = extraits[0];
        Assert.Equal(("Conditions de prêt", "1.0"), (premier.Titre, premier.Version));
        Assert.Contains("21 jours", premier.Contenu);
        Assert.StartsWith("Conditions de prêt, version 1.0, section", premier.Citation);
    }

    [Fact]
    public async Task Brouillons_et_archives_exclus()
    {
        var extraits = await (await f.CreateClient().GetAsync("api/documents/recherche?q=tarifs amende dinars")).LireAsync<List<ExtraitDto>>();
        Assert.DoesNotContain(extraits, e => e.Titre == "Projet de tarifs 2027");

        var biblio = await f.BibliothecaireAsync();
        var doc = await (await biblio.PostJsonAsync("api/documents", new DocumentRequete
        {
            Titre = "Note piscine", Type = TypeDocument.Autre, Version = "1.0", Contenu = "# Note piscine\n\n## Natation\nLa bibliotheque prete des palmes zorglub.",
        })).LireAsync<DocumentDto>();
        Assert.Empty(await Chercher("palmes zorglub"));                              // brouillon

        var publie = await (await biblio.PostJsonAsync($"api/documents/{doc.Id}/publier")).LireAsync<DocumentDto>();
        Assert.True(publie.NbFragments >= 1);
        Assert.Equal("Natation", Assert.Single(await Chercher("palmes zorglub")).Section);

        // Nouvelle version publiée : l'ancienne est archivée et ne sort plus.
        var v2 = await (await biblio.PostJsonAsync("api/documents", new DocumentRequete
        {
            Titre = "Note piscine", Type = TypeDocument.Autre, Version = "2.0", Contenu = "# Note piscine\n\n## Natation\nLes palmes zorglub ne sont plus pretees.",
        })).LireAsync<DocumentDto>();
        await biblio.PostJsonAsync($"api/documents/{v2.Id}/publier");
        Assert.Equal("2.0", Assert.Single(await Chercher("palmes zorglub")).Version);
        var ancien = await (await biblio.GetAsync($"api/documents/{doc.Id}")).LireAsync<DocumentDto>();
        Assert.Equal((StatutPublication.Archive, 0), (ancien.Statut, ancien.NbFragments));

        await biblio.PostJsonAsync($"api/documents/{v2.Id}/archiver");
        Assert.Empty(await Chercher("palmes zorglub"));
    }

    [Fact]
    public async Task Documents_reserves_aux_bibliothecaires()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await membre.GetAsync("api/documents")).StatusCode);
    }

    private async Task<List<ExtraitDto>> Chercher(string q) =>
        await (await f.CreateClient().GetAsync($"api/documents/recherche?q={Uri.EscapeDataString(q)}")).LireAsync<List<ExtraitDto>>();
}

public class AgentTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    private static Task<HttpResponseMessage> Poser(HttpClient c, string texte, int? conversation = null) =>
        c.PostJsonAsync("api/agent/messages", new QuestionAgentRequete { Texte = texte, ConversationId = conversation });

    [Fact]
    public async Task Anonyme_catalogue_oui_donnees_personnelles_non()
    {
        var anonyme = f.CreateClient();
        var catalogue = await (await Poser(anonyme, "Avez-vous des livres de Jules Verne ?")).LireAsync<ReponseAgentDto>();
        Assert.Contains("Vingt mille lieues", catalogue.Texte);
        Assert.Contains("rechercherLivres", catalogue.Outils);
        Assert.Equal("simule", catalogue.Moteur);

        var perso = await (await Poser(anonyme, "Quels sont mes emprunts ?")).LireAsync<ReponseAgentDto>();
        Assert.Contains("connectez-vous", perso.Texte, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Livre_absent_pas_d_invention()
    {
        var r = await (await Poser(f.CreateClient(), "Avez-vous le livre Harry Potter et la coupe de feu ?")).LireAsync<ReponseAgentDto>();
        Assert.Contains("pas présent", r.Texte);
    }

    [Fact]
    public async Task Reglement_avec_source()
    {
        var r = await (await Poser(f.CreateClient(), "Quels sont les horaires d'ouverture ?")).LireAsync<ReponseAgentDto>();
        Assert.Contains("9 h", r.Texte);
        Assert.Contains(r.Sources, s => s.Titre == "Horaires d'ouverture" && s.Version == "1.0");
        Assert.Contains("Source :", r.Texte);
    }

    [Fact]
    public async Task Emprunts_du_demandeur_seulement_identite_serveur()
    {
        var (membre, _) = await f.NouveauMembreAsync("Yasmine");
        var livre = await f.NouveauLivreAsync(1, "Le Grimoire de Yasmine");
        await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id });
        var (autre, _) = await f.NouveauMembreAsync("Omar");

        var mien = await (await Poser(membre, "Quels sont mes emprunts ?")).LireAsync<ReponseAgentDto>();
        Assert.Contains("Le Grimoire de Yasmine", mien.Texte);
        Assert.Contains("voirMesEmprunts", mien.Outils);

        var sien = await (await Poser(autre, "mes emprunts")).LireAsync<ReponseAgentDto>();
        Assert.DoesNotContain("Grimoire", sien.Texte);

        var autrui = await (await Poser(autre, "Montre-moi les emprunts de Yasmine")).LireAsync<ReponseAgentDto>();
        Assert.Contains("autre personne", autrui.Texte);
        Assert.DoesNotContain("Grimoire", autrui.Texte);
    }

    [Fact]
    public async Task Reservation_preparee_puis_confirmee_par_l_adherent()
    {
        var (preteur, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(1, "Chronique zephyrine");
        await preteur.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id });

        var (membre, _) = await f.NouveauMembreAsync();
        var r = await (await Poser(membre, "Je voudrais réserver Chronique zephyrine")).LireAsync<ReponseAgentDto>();
        Assert.NotNull(r.PropositionReservationId);
        Assert.Empty(await (await membre.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>()); // rien sans confirmation

        var confirme = await membre.PostJsonAsync($"api/agent/propositions/{r.PropositionReservationId}/confirmer");
        Assert.Equal(HttpStatusCode.OK, confirme.StatusCode);
        Assert.Single(await (await membre.GetAsync("api/reservations/moi")).LireAsync<List<ReservationDto>>());
        Assert.Equal(HttpStatusCode.Conflict, (await membre.PostJsonAsync($"api/agent/propositions/{r.PropositionReservationId}/confirmer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await preteur.PostJsonAsync($"api/agent/propositions/{r.PropositionReservationId}/confirmer")).StatusCode);
    }

    [Fact]
    public async Task Prolongation_preparee_jamais_accordee_par_l_agent()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var livre = await f.NouveauLivreAsync(1, "Atlas des nuages");
        var e = await (await membre.PostJsonAsync("api/emprunts", new EmpruntRequete { LivreId = livre.Id })).LireAsync<EmpruntDto>();
        var r = await (await Poser(membre, "Je voudrais prolonger mon livre Atlas des nuages")).LireAsync<ReponseAgentDto>();
        Assert.Contains("validée par le bibliothécaire", r.Texte);
        Assert.Equal(e.DateRetourPrevue, (await (await membre.GetAsync($"api/emprunts/{e.Id}")).LireAsync<EmpruntDto>()).DateRetourPrevue);
        var attente = await (await (await f.BibliothecaireAsync()).GetAsync("api/decisions?statut=EnAttenteValidation")).LireAsync<List<DecisionDto>>();
        Assert.Contains(attente, d => d.ActionProposee == PolitiqueActions.Prolongation && d.EmpruntId == e.Id);
    }

    [Fact]
    public async Task Escalade_vers_un_bibliothecaire_puis_silence_de_l_agent()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        var r = await (await Poser(membre, "Je veux parler à un bibliothécaire")).LireAsync<ReponseAgentDto>();
        Assert.True(r.Escalade);
        var suite = await (await Poser(membre, "Avez-vous Germinal ?", r.ConversationId)).LireAsync<ReponseAgentDto>();
        Assert.Equal("humain", suite.Moteur);
        var conv = await (await (await f.BibliothecaireAsync()).GetAsync($"api/gestion/conversations/{r.ConversationId}")).LireAsync<ConversationDetailDto>();
        Assert.Equal(EtatConversation.Humain, conv.Conversation.Etat);
    }

    [Fact]
    public async Task Bibliothecaire_resume_et_relance_preparee()
    {
        var biblio = await f.BibliothecaireAsync();
        var r = await (await Poser(biblio, "Résume la situation du jour")).LireAsync<ReponseAgentDto>();
        Assert.Contains("Situation du", r.Texte);
        var (membre, _) = await f.NouveauMembreAsync();
        var refus = await (await Poser(membre, "résumer la situation du jour")).LireAsync<ReponseAgentDto>();
        Assert.DoesNotContain("Situation du", refus.Texte);
    }

    [Fact]
    public async Task Outil_inconnu_interdit_et_journalise()
    {
        await f.AvecScopeAsync(async sp =>
        {
            var outils = sp.GetRequiredService<OutilsAgent>();
            var json = await outils.ExecuterAsync("enregistrerRetour", System.Text.Json.JsonDocument.Parse("{}").RootElement);
            Assert.Contains("interdite", json);
            var db = sp.GetRequiredService<MediaLibDbContext>();
            Assert.True(await db.Decisions.AnyAsync(d => d.ActionProposee == "enregistrerRetour" && d.Statut == StatutDecision.Interdite));
        });
    }

    [Fact]
    public async Task Journal_des_decisions_trace_outils_et_sources()
    {
        var r = await (await Poser(f.CreateClient(), "Comment arrêter les messages WhatsApp ? STOP")).LireAsync<ReponseAgentDto>();
        var d = await (await (await f.BibliothecaireAsync()).GetAsync($"api/decisions/{r.DecisionId}")).LireAsync<DecisionDto>();
        Assert.Equal((PolitiqueActions.ReponseConversation, StatutDecision.Executee), (d.ActionProposee, d.Statut));
        Assert.Contains("chercherReglement", d.Outils);
        Assert.NotEmpty(d.Sources);
        Assert.Contains("simule", d.Donnees);
    }
}

public class AgentModeClaudeSansCleTests : IDisposable
{
    private readonly MediaLibFactory _f = new();

    public AgentModeClaudeSansCleTests()
    {
        _f.Reglages["Agent:Mode"] = "claude";
        _f.Reglages["Agent:CleApi"] = "";
        _f.Reglages["Agent:MessagesParMinute"] = "2";
    }

    [Fact]
    public async Task Repli_sur_le_moteur_simule_et_limite_de_debit()
    {
        var c = _f.CreateClient();
        var r = await (await c.PostJsonAsync("api/agent/messages", new QuestionAgentRequete { Texte = "Avez-vous Germinal ?" })).LireAsync<ReponseAgentDto>();
        Assert.Equal(BibliothecaireIA.SecoursSansCle, r.Moteur);
        Assert.Contains("Germinal", r.Texte);

        // Même conversation anonyme : 2 messages par minute au plus.
        await c.PostJsonAsync("api/agent/messages", new QuestionAgentRequete { Texte = "Et Candide ?", ConversationId = r.ConversationId });
        var limite = await (await c.PostJsonAsync("api/agent/messages", new QuestionAgentRequete { Texte = "Et Zadig ?", ConversationId = r.ConversationId })).LireAsync<ReponseAgentDto>();
        Assert.Equal(BibliothecaireIA.Limite, limite.Moteur);
    }

    public void Dispose() => _f.Dispose();
}

public class RelancesTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    private DateOnly Aujourdhui => DateOnly.FromDateTime(f.Horloge.GetUtcNow().UtcDateTime);

    private Task<int> EmpruntAsync(string utilisateurId, int echeanceDansJours) => f.AvecScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<MediaLibDbContext>();
        var livre = new Livre { Titre = $"Relance {Guid.NewGuid():N}"[..20], Isbn = Seed.Isbn13("979" + Random.Shared.NextInt64(100_000_000, 999_999_999)), Annee = 2000, NbExemplaires = 1, NbDisponibles = 0, AuteurId = 1, CategorieId = 1 };
        var e = new Emprunt { Livre = livre, UtilisateurId = utilisateurId, DateEmprunt = Aujourdhui.AddDays(echeanceDansJours - 21), DateRetourPrevue = Aujourdhui.AddDays(echeanceDansJours) };
        db.Emprunts.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    });

    private Task<List<Relance>> RelancesAsync(int empruntId) => f.AvecScopeAsync(sp =>
        sp.GetRequiredService<MediaLibDbContext>().Relances.Include(r => r.Decision).Where(r => r.EmpruntId == empruntId).ToListAsync());

    [Fact]
    public async Task Detection_J2_J1_automatiques_et_J7_en_validation_sans_doublon()
    {
        var (_, u) = await f.NouveauMembreAsync();
        var j2 = await EmpruntAsync(u.Id, 2);
        var j1 = await EmpruntAsync(u.Id, -1);
        var j7 = await EmpruntAsync(u.Id, -8);
        var biblio = await f.BibliothecaireAsync();

        await biblio.PostJsonAsync("api/relances/traitement");
        Assert.Equal((NiveauRelance.RappelAvantEcheance, StatutRelance.Envoyee), Single(await RelancesAsync(j2)));
        Assert.Equal((NiveauRelance.RelanceRetardJ1, StatutRelance.Envoyee), Single(await RelancesAsync(j1)));
        var r7 = Assert.Single(await RelancesAsync(j7));
        Assert.Equal((NiveauRelance.RelanceRetardJ7, StatutRelance.EnAttenteValidation, StatutDecision.EnAttenteValidation), (r7.Niveau, r7.Statut, r7.Decision!.Statut));

        // Un second passage ne crée ni ne renvoie rien.
        var envoisAvant = f.Boite.Envoyes.Count;
        await biblio.PostJsonAsync("api/relances/traitement");
        Assert.Single(await RelancesAsync(j2));
        Assert.Equal(envoisAvant, f.Boite.Envoyes.Count);

        // Validation humaine de la J+7 : envoyée.
        var d = await (await biblio.PostJsonAsync($"api/decisions/{r7.DecisionId}/approuver", new ValidationDecisionRequete())).LireAsync<DecisionDto>();
        Assert.Equal(StatutDecision.Executee, d.Statut);
        Assert.Equal(StatutRelance.Envoyee, Assert.Single(await RelancesAsync(j7)).Statut);
    }

    private static (NiveauRelance, StatutRelance) Single(List<Relance> l) { var r = Assert.Single(l); return (r.Niveau, r.Statut); }

    [Fact]
    public async Task Reessai_puis_escalade_apres_le_maximum_de_tentatives()
    {
        var (_, u) = await f.NouveauMembreAsync();
        var id = await EmpruntAsync(u.Id, 1);
        await f.AvecScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<RelanceService>();
            f.Boite.SimulerEchecs(1);
            await service.DetecterAsync();
            var relance = (await RelancesAsync(id)).Single();
            Assert.Equal(StatutRelance.Echec, await service.EnvoyerAsync(relance.Id));
            Assert.Equal(StatutRelance.Envoyee, await service.EnvoyerAsync(relance.Id)); // réessai réussi
            Assert.Equal(StatutRelance.Envoyee, await service.EnvoyerAsync(relance.Id)); // rejouer ne renvoie rien
        });
        var r = (await RelancesAsync(id)).Single();
        Assert.Equal(2, r.Tentatives);
        var messages = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Messages.CountAsync(m => m.CleIdempotence == r.CleIdempotence));
        Assert.Equal(1, messages); // une clé = un seul message

        var (_, u2) = await f.NouveauMembreAsync();
        var id2 = await EmpruntAsync(u2.Id, 1);
        f.Boite.SimulerEchecs(10);
        try
        {
            await f.AvecScopeAsync(async sp =>
            {
                var service = sp.GetRequiredService<RelanceService>();
                await service.DetecterAsync();
                var relance = (await RelancesAsync(id2)).Single();
                for (var i = 0; i < 3; i++) await service.EnvoyerAsync(relance.Id);
            });
        }
        finally
        {
            f.Boite.Vider();
        }
        var escaladee = (await RelancesAsync(id2)).Single();
        Assert.Equal((StatutRelance.Escaladee, 3, StatutDecision.Escaladee), (escaladee.Statut, escaladee.Tentatives, escaladee.Decision!.Statut));
    }

    [Fact]
    public async Task Livre_rendu_annule_la_relance_en_attente()
    {
        var (_, u) = await f.NouveauMembreAsync();
        var id = await EmpruntAsync(u.Id, -10);
        var biblio = await f.BibliothecaireAsync();
        await biblio.PostJsonAsync("api/relances/traitement");
        Assert.Equal(StatutRelance.EnAttenteValidation, (await RelancesAsync(id)).Single().Statut);
        await biblio.PostJsonAsync($"api/emprunts/{id}/retour");
        var r = (await RelancesAsync(id)).Single();
        Assert.Equal((StatutRelance.Annulee, StatutDecision.Annulee), (r.Statut, r.Decision!.Statut));
    }

    [Fact]
    public async Task Idempotence_de_la_notification()
    {
        var (_, u) = await f.NouveauMembreAsync();
        await f.AvecScopeAsync(async sp =>
        {
            var com = sp.GetRequiredService<ServiceCommunication>();
            var utilisateur = await sp.GetRequiredService<MediaLibDbContext>().Users.FindAsync(u.Id);
            var m1 = await com.NotifierAsync(utilisateur!, ModeleMessage.RappelEcheance, ["A", "B", "C"], "cle-test-idem");
            var m2 = await com.NotifierAsync(utilisateur!, ModeleMessage.RappelEcheance, ["A", "B", "C"], "cle-test-idem");
            Assert.Equal(m1.Id, m2.Id);
            Assert.Equal(Canal.Email, m1.Canal);
        });
    }

    [Fact]
    public async Task Repli_email_si_WhatsApp_echoue()
    {
        var (_, u) = await f.NouveauMembreAsync();
        await f.AvecScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MediaLibDbContext>();
            var utilisateur = (await db.Users.FindAsync(u.Id))!;
            utilisateur.Telephone = $"+2165{Random.Shared.Next(1000000, 9999999)}";
            utilisateur.TelephoneVerifieLe = DateTime.UtcNow;
            utilisateur.ConsentementWhatsappLe = DateTime.UtcNow;
            utilisateur.CanalPrefere = Canal.WhatsApp;
            await db.SaveChangesAsync();
            f.Boite.SimulerEchecs(1, canal: Canal.WhatsApp);
            try
            {
                var m = await sp.GetRequiredService<ServiceCommunication>().NotifierAsync(utilisateur, ModeleMessage.RappelEcheance, ["A", "B", "C"], $"repli-{Guid.NewGuid():N}");
                Assert.Equal((Canal.Email, StatutMessage.Envoye), (m.Canal, m.Statut));
            }
            finally
            {
                f.Boite.Vider();
            }
        });
    }
}

public class WebhookWhatsAppTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    private static string Signer(string corps) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(MediaLibFactory.SecretWebhook), Encoding.UTF8.GetBytes(corps)));

    private static string Message(string de, string texte, string? id = null) =>
        $$$"""{"entry":[{"changes":[{"value":{"messages":[{"id":"{{{id ?? "wamid." + Guid.NewGuid().ToString("N")}}}","from":"{{{de}}}","timestamp":"1700000000","type":"text","text":{"body":"{{{texte}}}"}}]}}]}]}""";

    private async Task<HttpResponseMessage> EnvoyerAsync(string corps, string? signature = null)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, "api/webhooks/whatsapp") { Content = new StringContent(corps, Encoding.UTF8, "application/json") };
        if (signature is not null) requete.Headers.Add("X-Hub-Signature-256", signature);
        return await f.CreateClient().SendAsync(requete);
    }

    private static string Numero() => $"2169{Random.Shared.Next(1000000, 9999999)}";

    [Fact]
    public async Task Verification_d_abonnement()
    {
        var ok = await f.CreateClient().GetAsync($"api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={MediaLibFactory.JetonVerification}&hub.challenge=42");
        Assert.Equal("42", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClient().GetAsync("api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=faux&hub.challenge=42")).StatusCode);
    }

    [Fact]
    public async Task Signature_absente_ou_invalide_403()
    {
        var corps = Message(Numero(), "Bonjour");
        Assert.Equal(HttpStatusCode.Forbidden, (await EnvoyerAsync(corps)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EnvoyerAsync(corps, "sha256=" + new string('a', 64))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EnvoyerAsync(corps, Signer(corps + "x"))).StatusCode);
    }

    [Fact]
    public async Task Message_entrant_reponse_de_l_agent_et_idempotence()
    {
        var numero = Numero();
        var corps = Message(numero, "Avez-vous Germinal ?", "wamid.unique-" + numero);
        Assert.Equal(HttpStatusCode.OK, (await EnvoyerAsync(corps, Signer(corps))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EnvoyerAsync(corps, Signer(corps))).StatusCode); // rejeu Meta

        var conv = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Conversations.Include(c => c.Messages).SingleAsync(c => c.Telephone == "+" + numero));
        Assert.Single(conv.Messages, m => m.Sens == Sens.Entrant);
        Assert.Contains(conv.Messages, m => m.Sens == Sens.Sortant && m.Contenu.Contains("Germinal") && m.Statut == StatutMessage.Envoye);
    }

    [Fact]
    public async Task Liaison_du_numero_puis_STOP_retire_le_consentement()
    {
        var (membre, u) = await f.NouveauMembreAsync("Walid");
        Assert.Equal(HttpStatusCode.Conflict, (await membre.PostJsonAsync("api/compte/whatsapp/liaison")).StatusCode); // pas de consentement
        await membre.PutJsonAsync("api/compte/whatsapp/consentement", new ConsentementRequete { Consentement = true });
        var liaison = await (await membre.PostJsonAsync("api/compte/whatsapp/liaison")).LireAsync<LiaisonWhatsappDto>();
        Assert.Contains("wa.me", liaison.Lien);

        var numero = Numero();
        var corps = Message(numero, $"LIAISON {liaison.Code}");
        await EnvoyerAsync(corps, Signer(corps));
        var moi = await (await membre.GetAsync("api/auth/moi")).LireAsync<UtilisateurDto>();
        Assert.Equal(("+" + numero, true, true), (moi.Telephone, moi.TelephoneVerifie, moi.ConsentementWhatsapp));
        var profil = await membre.PutJsonAsync("api/compte/profil", new ProfilRequete { Prenom = "Walid", Nom = "M", Telephone = "+" + numero, CanalPrefere = Canal.WhatsApp });
        Assert.Equal(Canal.WhatsApp, (await profil.LireAsync<UtilisateurDto>()).CanalPrefere);

        var stop = Message(numero, "STOP");
        await EnvoyerAsync(stop, Signer(stop));
        moi = await (await membre.GetAsync("api/auth/moi")).LireAsync<UtilisateurDto>();
        Assert.False(moi.ConsentementWhatsapp);
        Assert.Equal(Canal.Email, moi.CanalPrefere);
        _ = u;
    }

    [Fact]
    public async Task Statuts_Meta_appliques()
    {
        var numero = Numero();
        var corps = Message(numero, "Bonjour");
        await EnvoyerAsync(corps, Signer(corps));
        var idExterne = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Messages
            .Where(m => m.Conversation.Telephone == "+" + numero && m.Sens == Sens.Sortant).Select(m => m.IdentifiantExterne!).FirstAsync());
        var statut = $$$"""{"entry":[{"changes":[{"value":{"statuses":[{"id":"{{{idExterne}}}","status":"read","timestamp":"1700000100"}]}}]}]}""";
        await EnvoyerAsync(statut, Signer(statut));
        var s = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Messages.Where(m => m.IdentifiantExterne == idExterne).Select(m => m.Statut).FirstAsync());
        Assert.Equal(StatutMessage.Lu, s);
    }

    [Fact]
    public async Task Fenetre_24h_et_reprise_par_un_bibliothecaire()
    {
        var numero = Numero();
        var corps = Message(numero, "Bonjour");
        await EnvoyerAsync(corps, Signer(corps));
        var convId = await f.AvecScopeAsync(sp => sp.GetRequiredService<MediaLibDbContext>().Conversations.Where(c => c.Telephone == "+" + numero).Select(c => c.Id).FirstAsync());
        var biblio = await f.BibliothecaireAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await biblio.PostJsonAsync($"api/gestion/conversations/{convId}/reprendre")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await biblio.PostJsonAsync($"api/gestion/conversations/{convId}/messages", new ReponseBibliothecaireRequete { Texte = "Je m'en occupe." })).StatusCode);

        // L'agent se tait pendant la reprise humaine.
        var envoisAvant = f.Boite.Envoyes.Count;
        var suite = Message(numero, "Avez-vous Candide ?");
        await EnvoyerAsync(suite, Signer(suite));
        Assert.Equal(envoisAvant, f.Boite.Envoyes.Count);

        f.Horloge.Advance(TimeSpan.FromHours(25));
        try
        {
            var r = await biblio.PostJsonAsync($"api/gestion/conversations/{convId}/messages", new ReponseBibliothecaireRequete { Texte = "Toujours là ?" });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("24 h", await r.Content.ReadAsStringAsync());
        }
        finally
        {
            f.Horloge.SetUtcNow(DateTimeOffset.UtcNow);
        }
    }
}
