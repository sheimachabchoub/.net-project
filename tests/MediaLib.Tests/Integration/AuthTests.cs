using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using MediaLib.Api.Data;
using MediaLib.Shared;
using MediaLib.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace MediaLib.Tests.Integration;

public class AuthTests(MediaLibFactory f) : IClassFixture<MediaLibFactory>
{
    [Fact]
    public async Task Inscription_renvoie_201_avec_Location_et_role_Membre()
    {
        var email = $"insc-{Guid.NewGuid():N}@test.local";
        var r = await f.CreateClient().PostJsonAsync("api/auth/inscription",
            new InscriptionRequete { Email = email, Prenom = "Ines", Nom = "Test", MotDePasse = "Bonjour12", ConfirmationMotDePasse = "Bonjour12" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        Assert.Equal([Roles.Membre], (await r.LireAsync<UtilisateurDto>()).Roles);
    }

    [Theory]
    [InlineData("court1A")]      // < 8
    [InlineData("sansmajuscule1")]
    [InlineData("SansChiffre")]
    public async Task Mot_de_passe_trop_faible_400(string mdp)
    {
        var r = await f.CreateClient().PostJsonAsync("api/auth/inscription",
            new InscriptionRequete { Email = $"x-{Guid.NewGuid():N}@test.local", Prenom = "A", Nom = "B", MotDePasse = mdp, ConfirmationMotDePasse = mdp });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Email_deja_utilise_409()
    {
        var r = await f.CreateClient().PostJsonAsync("api/auth/inscription",
            new InscriptionRequete { Email = ComptesDemo.MembreEmail, Prenom = "A", Nom = "B", MotDePasse = "Bonjour12", ConfirmationMotDePasse = "Bonjour12" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Connexion_emet_un_access_token_court_et_valide()
    {
        var j = await f.ConnecterAsync(ComptesDemo.MembreEmail, ComptesDemo.MembreMotDePasse);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(j.AccessToken);
        Assert.Equal("MediaLib.Api", jwt.Issuer);
        Assert.Contains("MediaLib.Web", jwt.Audiences);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalMinutes, 14, 16);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == Roles.Membre);
        Assert.True(j.RefreshExpireLe > j.ExpireLe.AddDays(6));

        var moi = await f.Client(j.AccessToken).GetAsync("api/auth/moi");
        Assert.Equal(HttpStatusCode.OK, moi.StatusCode);
        Assert.Equal(ComptesDemo.MembreEmail, (await moi.LireAsync<UtilisateurDto>()).Email);
    }

    [Fact]
    public async Task Sans_jeton_401_et_mauvais_mot_de_passe_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("api/auth/moi")).StatusCode);
        var r = await f.CreateClient().PostJsonAsync("api/auth/connexion", new ConnexionRequete { Email = ComptesDemo.MembreEmail, MotDePasse = "Mauvais123" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("MediaLib.Api", "MediaLib.Web", MediaLibFactory.CleJwt, -5)]                         // expiré
    [InlineData("Pirate", "MediaLib.Web", MediaLibFactory.CleJwt, 10)]                                // mauvais émetteur
    [InlineData("MediaLib.Api", "AutreAudience", MediaLibFactory.CleJwt, 10)]                         // mauvaise audience
    [InlineData("MediaLib.Api", "MediaLib.Web", "une-autre-cle-de-signature-de-32-octets-au-moins", 10)] // mauvaise signature
    public async Task Jeton_invalide_rejete_401(string emetteur, string audience, string cle, int minutes)
    {
        var maintenant = DateTime.UtcNow;
        var jeton = new JwtSecurityToken(emetteur, audience, [new("sub", "x"), new("role", Roles.Admin)],
            notBefore: maintenant.AddMinutes(-30), expires: maintenant.AddMinutes(minutes),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cle)), SecurityAlgorithms.HmacSha256));
        var r = await f.Client(new JwtSecurityTokenHandler().WriteToken(jeton)).GetAsync("api/admin/utilisateurs");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Verrouillage_apres_5_echecs()
    {
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        await f.CreateClient().PostJsonAsync("api/auth/inscription", new InscriptionRequete { Email = email, Prenom = "L", Nom = "K", MotDePasse = "Valide123", ConfirmationMotDePasse = "Valide123" });
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/auth/connexion", new ConnexionRequete { Email = email, MotDePasse = "Faux12345" })).StatusCode);
        // Même le bon mot de passe est refusé : le compte est verrouillé.
        var r = await f.CreateClient().PostJsonAsync("api/auth/connexion", new ConnexionRequete { Email = email, MotDePasse = "Valide123" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("verrouill", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_tourne_et_l_ancien_jeton_est_revoque()
    {
        var j1 = await f.ConnecterAsync(ComptesDemo.MembreEmail, ComptesDemo.MembreMotDePasse);
        var r = await f.CreateClient().PostJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = j1.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j2 = await r.LireAsync<JetonsReponse>();
        Assert.NotEqual(j1.RefreshToken, j2.RefreshToken);

        // Réutiliser l'ancien refresh token : refusé, et toute la famille est révoquée.
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = j1.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = j2.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Deconnexion_revoque_le_refresh_token()
    {
        var j = await f.ConnecterAsync(ComptesDemo.MembreEmail, ComptesDemo.MembreMotDePasse);
        var r = await f.Client(j.AccessToken).PostJsonAsync("api/auth/deconnexion", new RefreshRequete { RefreshToken = j.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = j.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Refresh_expire_401()
    {
        var j = await f.ConnecterAsync(ComptesDemo.BibliothecaireEmail, ComptesDemo.BibliothecaireMotDePasse);
        f.Horloge.Advance(TimeSpan.FromDays(8));
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostJsonAsync("api/auth/refresh", new RefreshRequete { RefreshToken = j.RefreshToken })).StatusCode);
        }
        finally
        {
            f.Horloge.SetUtcNow(DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Roles_admin_et_autorisations()
    {
        var (membre, _) = await f.NouveauMembreAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await membre.GetAsync("api/admin/utilisateurs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await f.BibliothecaireAsync()).GetAsync("api/admin/utilisateurs")).StatusCode);
        var admin = await f.AdminAsync();
        var liste = await (await admin.GetAsync("api/admin/utilisateurs")).LireAsync<List<UtilisateurAdminDto>>();
        Assert.Contains(liste, u => u.Email == ComptesDemo.BibliothecaireEmail && u.Roles.Contains(Roles.Bibliothecaire));
    }

    [Fact]
    public async Task Admin_change_les_roles()
    {
        var (_, u) = await f.NouveauMembreAsync();
        var admin = await f.AdminAsync();
        var r = await admin.PutJsonAsync($"api/admin/utilisateurs/{u.Id}/roles", new RolesRequete { Roles = [Roles.Membre, Roles.Bibliothecaire] });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(Roles.Bibliothecaire, (await r.LireAsync<UtilisateurAdminDto>()).Roles);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync($"api/admin/utilisateurs/{u.Id}/roles", new RolesRequete { Roles = ["Pirate"] })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutJsonAsync("api/admin/utilisateurs/inconnu/roles", new RolesRequete { Roles = [Roles.Membre] })).StatusCode);
    }
}

public class LimiteDebitTests : IDisposable
{
    private readonly MediaLibFactory _f = new();

    public LimiteDebitTests() => _f.Reglages["LimiteDebit:AuthParMinute"] = "3";

    [Fact]
    public async Task Trop_de_tentatives_de_connexion_429()
    {
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await _f.CreateClient().PostJsonAsync("api/auth/connexion", new ConnexionRequete { Email = "x@test.local", MotDePasse = "Faux12345" })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }

    public void Dispose() => _f.Dispose();
}
