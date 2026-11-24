using System.Security.Cryptography;
using System.Text;
using MediaLib.Api.Agent;
using MediaLib.Api.Communication;
using MediaLib.Api.Configuration;
using MediaLib.Api.Connaissance;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MediaLib.Tests.Unitaires;

public class IsbnTests
{
    [Theory]
    [InlineData("0306406152")]
    [InlineData("0-306-40615-2")]
    [InlineData("080442957X")]
    [InlineData("9780306406157")]
    [InlineData("978-2-07-036002-4")]
    public void Isbn_valides(string isbn) => Assert.True(IsbnValidateur.EstValide(isbn));

    [Theory]
    [InlineData("0306406153")]       // mauvaise clé ISBN-10
    [InlineData("9780306406158")]    // mauvaise clé ISBN-13
    [InlineData("97803064061")]      // longueur
    [InlineData("X306406152")]       // X mal placé
    [InlineData("")]
    public void Isbn_invalides(string isbn) => Assert.False(IsbnValidateur.EstValide(isbn));

    [Fact]
    public void Attribut_isbn_et_normalisation()
    {
        Assert.True(new IsbnAttribute().IsValid("978-0-306-40615-7"));
        Assert.False(new IsbnAttribute().IsValid("1234567890"));
        Assert.Equal("9780306406157", IsbnValidateur.Normaliser("978-0 306-40615-7"));
    }

    [Fact]
    public void Le_seed_produit_des_isbn13_valides() => Assert.True(IsbnValidateur.EstValide(Seed_Isbn("978207000001")));

    private static string Seed_Isbn(string douze) => MediaLib.Api.Data.Seed.Isbn13(douze);
}

public class PolitiqueActionsTests
{
    private readonly PolitiqueActions _politique = new();

    [Fact]
    public void Rappel_J_moins_2_et_relance_J1_sont_automatiques()
    {
        Assert.Equal(Classement.Automatique, _politique.ClasserRelance(NiveauRelance.RappelAvantEcheance));
        Assert.Equal(Classement.Automatique, _politique.ClasserRelance(NiveauRelance.RelanceRetardJ1));
    }

    [Fact]
    public void Relance_J7_et_prolongation_exigent_une_validation()
    {
        Assert.Equal(Classement.ValidationRequise, _politique.ClasserRelance(NiveauRelance.RelanceRetardJ7));
        Assert.Equal(Classement.ValidationRequise, _politique.Classer(PolitiqueActions.Prolongation));
        Assert.Equal(Classement.ValidationRequise, _politique.Classer(PolitiqueActions.MessageLibre));
    }

    [Theory]
    [InlineData(PolitiqueActions.EmpruntEnregistrer)]
    [InlineData(PolitiqueActions.EmpruntRetour)]
    [InlineData(PolitiqueActions.DonneesSupprimer)]
    [InlineData(PolitiqueActions.CatalogueModifier)]
    [InlineData("action.jamais.vue")]
    public void Emprunt_retour_suppression_et_action_inconnue_sont_interdits(string action) =>
        Assert.Equal(Classement.Interdite, _politique.Classer(action));
}

public class EmpruntDomaineTests
{
    private static readonly DateOnly Jour = new(2026, 11, 24);

    [Fact]
    public void Statut_selon_l_echeance()
    {
        Assert.Equal(StatutEmprunt.EnCours, new Emprunt { DateRetourPrevue = Jour.AddDays(10) }.Statut(Jour, 2));
        Assert.Equal(StatutEmprunt.EcheanceProche, new Emprunt { DateRetourPrevue = Jour.AddDays(2) }.Statut(Jour, 2));
        Assert.Equal(StatutEmprunt.EnRetard, new Emprunt { DateRetourPrevue = Jour.AddDays(-1) }.Statut(Jour, 2));
        Assert.Equal(StatutEmprunt.Rendu, new Emprunt { DateRetourPrevue = Jour.AddDays(-1), DateRetourReelle = Jour }.Statut(Jour, 2));
    }

    [Fact]
    public void Jours_de_retard()
    {
        var e = new Emprunt { DateRetourPrevue = Jour.AddDays(-5) };
        Assert.True(e.EstEnRetard(Jour));
        Assert.Equal(5, e.JoursRetard(Jour));
        Assert.Equal(0, new Emprunt { DateRetourPrevue = Jour }.JoursRetard(Jour));
    }
}

public class WhatsAppDomaineTests
{
    [Fact]
    public void Fenetre_de_24_heures()
    {
        var maintenant = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var c = new Conversation { Canal = Canal.WhatsApp, DernierMessageEntrantLe = maintenant.AddHours(-23) };
        Assert.True(c.FenetreOuverte(maintenant));
        c.DernierMessageEntrantLe = maintenant.AddHours(-25);
        Assert.False(c.FenetreOuverte(maintenant));
        Assert.False(new Conversation { Canal = Canal.WhatsApp }.FenetreOuverte(maintenant));
        Assert.True(new Conversation { Canal = Canal.Web }.FenetreOuverte(maintenant));
    }

    [Fact]
    public void Les_statuts_Meta_ne_reculent_jamais()
    {
        var m = new Message { Statut = StatutMessage.Envoye };
        Assert.True(m.AppliquerStatutExterne(StatutMessage.Lu, DateTime.UtcNow, null));
        Assert.False(m.AppliquerStatutExterne(StatutMessage.Delivre, DateTime.UtcNow, null));
        Assert.Equal(StatutMessage.Lu, m.Statut);
        Assert.True(m.AppliquerStatutExterne(StatutMessage.Echec, DateTime.UtcNow, "numéro invalide"));
        Assert.Equal("numéro invalide", m.Erreur);
    }

    [Fact]
    public void Droit_WhatsApp_exige_numero_valide_lie_et_consentement()
    {
        var u = new Utilisateur { Telephone = "+21650123456", TelephoneVerifieLe = DateTime.UtcNow, ConsentementWhatsappLe = DateTime.UtcNow };
        Assert.True(u.PeutRecevoirWhatsapp);
        u.ConsentementWhatsappLe = null;
        Assert.False(u.PeutRecevoirWhatsapp);
        u.ConsentementWhatsappLe = DateTime.UtcNow;
        u.TelephoneVerifieLe = null;
        Assert.False(u.PeutRecevoirWhatsapp);
        Assert.False(new Utilisateur { Telephone = "0650", TelephoneVerifieLe = DateTime.UtcNow, ConsentementWhatsappLe = DateTime.UtcNow }.PeutRecevoirWhatsapp);
    }

    [Fact]
    public void Code_de_liaison_extrait_du_message()
    {
        Assert.Equal("123456", LiaisonWhatsApp.ExtraireCode("LIAISON 123456"));
        Assert.Equal("000042", LiaisonWhatsApp.ExtraireCode("  liaison 000042 "));
        Assert.Null(LiaisonWhatsApp.ExtraireCode("LIAISON 12345"));
        Assert.Null(LiaisonWhatsApp.ExtraireCode("bonjour"));
    }

    [Fact]
    public void Signature_du_webhook()
    {
        var analyseur = new AnalyseurWebhookWhatsApp(Options.Create(new WhatsAppOptions { SecretApplication = "s3cret", JetonVerification = "v" }));
        const string corps = "{\"entry\":[]}";
        var bonne = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cret"), Encoding.UTF8.GetBytes(corps)));
        Assert.True(analyseur.SignatureValide(corps, bonne));
        Assert.False(analyseur.SignatureValide(corps, "sha256=" + new string('0', 64)));
        Assert.False(analyseur.SignatureValide(corps, null));
        Assert.False(analyseur.SignatureValide(corps + " ", bonne));
        Assert.True(analyseur.AbonnementValide("subscribe", "v"));
        Assert.False(analyseur.AbonnementValide("subscribe", "faux"));
    }

    [Fact]
    public void Analyse_des_messages_et_statuts_Meta()
    {
        var analyseur = new AnalyseurWebhookWhatsApp(Options.Create(new WhatsAppOptions()));
        var (messages, statuts) = analyseur.Analyser("""
            {"entry":[{"changes":[{"value":{
              "messages":[{"id":"wamid.1","from":"21650123456","timestamp":"1700000000","type":"text","text":{"body":"Bonjour"}}],
              "statuses":[{"id":"wamid.0","status":"delivered","timestamp":"1700000001"}]}}]}]}
            """);
        var m = Assert.Single(messages);
        Assert.Equal(("wamid.1", "21650123456", "Bonjour"), (m.IdentifiantMeta, m.De, m.Texte));
        Assert.Equal("delivered", Assert.Single(statuts).Statut);
    }
}

public class RagUnitaireTests
{
    [Fact]
    public void Normalisation_sans_accents_ni_mots_vides()
    {
        var termes = Normaliseur.Termes("Quelle est la durée des prêts à la Bibliothèque ?");
        Assert.Contains(Normaliseur.Raciner("duree"), termes);
        Assert.Contains("pret", termes);
        Assert.DoesNotContain("la", termes);
        Assert.DoesNotContain("quelle", termes);
        Assert.Equal(Normaliseur.Raciner("emprunter"), Normaliseur.Raciner("empruntes"));
    }

    [Fact]
    public void Decoupage_par_sections_avec_rang()
    {
        var sections = IndexeurConnaissance.Decouper("# Doc\n\nIntro.\n\n## Durée\nVingt et un jours.\n\n### Exception\nRien.\n\n## Horaires\n9 h - 18 h");
        Assert.Equal(["Doc", "Durée", "Durée › Exception", "Horaires"], sections.Select(s => s.Section).ToArray());
        Assert.Equal("Vingt et un jours.", sections[1].Contenu);
    }
}

public class LimiteurAgentTests
{
    [Fact]
    public void Limite_par_demandeur_et_plafond_quotidien()
    {
        var horloge = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        var limiteur = new LimiteurAgent(horloge, Options.Create(new AgentOptions { MessagesParMinute = 2, PlafondQuotidien = 3 }));

        Assert.True(limiteur.AccepterMessage("a"));
        Assert.True(limiteur.AccepterMessage("a"));
        Assert.False(limiteur.AccepterMessage("a"));
        Assert.True(limiteur.AccepterMessage("b"));           // un autre demandeur n'est pas pénalisé
        horloge.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiteur.AccepterMessage("a"));            // fenêtre glissante écoulée

        Assert.True(limiteur.ConsommerAppelModele());
        Assert.True(limiteur.ConsommerAppelModele());
        Assert.True(limiteur.ConsommerAppelModele());
        Assert.False(limiteur.ConsommerAppelModele());         // plafond quotidien atteint
        horloge.Advance(TimeSpan.FromDays(1));
        Assert.True(limiteur.ConsommerAppelModele());          // nouveau jour
    }
}
