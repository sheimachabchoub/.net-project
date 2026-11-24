using MediaLib.Api.Connaissance;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Data;

/// <summary>Comptes de démonstration (identifiants de TEST uniquement, documentés dans le README).</summary>
public static class ComptesDemo
{
    public const string MembreEmail = "membre@medialib.local";
    public const string MembreMotDePasse = "Membre123";
    public const string BibliothecaireEmail = "biblio@medialib.local";
    public const string BibliothecaireMotDePasse = "Biblio123";
    public const string AdminEmail = "admin@medialib.local";
    public const string AdminMotDePasse = "Admin1234";
}

/// <summary>Jeu de données initial : 10 auteurs, 5 catégories, 30 livres, 3 comptes, documents de connaissance.</summary>
public class Seed(
    MediaLibDbContext db,
    UserManager<Utilisateur> users,
    RoleManager<IdentityRole> roles,
    IndexeurConnaissance indexeur,
    TimeProvider horloge,
    ILogger<Seed> logger)
{
    public async Task ExecuterAsync(bool avecEmpruntsDemo = true, CancellationToken ct = default)
    {
        foreach (var role in Roles.Tous)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var membre = await CompteAsync(ComptesDemo.MembreEmail, ComptesDemo.MembreMotDePasse, "Sarra", "Ben Ali", Roles.Membre);
        await CompteAsync(ComptesDemo.BibliothecaireEmail, ComptesDemo.BibliothecaireMotDePasse, "Karim", "Trabelsi", Roles.Bibliothecaire);
        await CompteAsync(ComptesDemo.AdminEmail, ComptesDemo.AdminMotDePasse, "Amel", "Mansour", Roles.Admin);

        if (!await db.Livres.AnyAsync(ct))
        {
            await CatalogueAsync(ct);
            if (avecEmpruntsDemo) await EmpruntsDemoAsync(membre, ct);
            logger.LogInformation("Catalogue initialisé : {Auteurs} auteurs, {Categories} catégories, {Livres} livres.",
                await db.Auteurs.CountAsync(ct), await db.Categories.CountAsync(ct), await db.Livres.CountAsync(ct));
        }
        if (!await db.Documents.AnyAsync(ct)) await DocumentsAsync(ct);
    }

    private async Task<Utilisateur> CompteAsync(string email, string motDePasse, string prenom, string nom, string role)
    {
        var u = await users.FindByEmailAsync(email);
        if (u is null)
        {
            u = new Utilisateur
            {
                UserName = email, Email = email, EmailConfirmed = true, Prenom = prenom, Nom = nom,
                InscritLe = horloge.Maintenant(), LockoutEnabled = true,
            };
            var r = await users.CreateAsync(u, motDePasse);
            if (!r.Succeeded) throw new InvalidOperationException(string.Join(" ", r.Errors.Select(e => e.Description)));
        }
        if (!await users.IsInRoleAsync(u, role)) await users.AddToRoleAsync(u, role);
        return u;
    }

    /// <summary>Calcule la clé de contrôle d'un ISBN-13 à partir de ses 12 premiers chiffres.</summary>
    public static string Isbn13(string douzeChiffres)
    {
        var somme = 0;
        for (var i = 0; i < 12; i++) somme += (douzeChiffres[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return douzeChiffres + ((10 - somme % 10) % 10);
    }

    private async Task CatalogueAsync(CancellationToken ct)
    {
        var categories = new Dictionary<string, Categorie>
        {
            ["Roman"] = new() { Nom = "Roman", Description = "Romans classiques et contemporains" },
            ["Science-fiction"] = new() { Nom = "Science-fiction", Description = "Anticipation, futurs possibles" },
            ["Policier"] = new() { Nom = "Policier", Description = "Enquêtes et romans noirs" },
            ["Jeunesse"] = new() { Nom = "Jeunesse", Description = "Livres pour les jeunes lecteurs" },
            ["Philosophie"] = new() { Nom = "Philosophie", Description = "Essais et pensée" },
        };
        var auteurs = new Dictionary<string, Auteur>
        {
            ["Hugo"] = new() { Prenom = "Victor", Nom = "Hugo", Biographie = "Poète, dramaturge et romancier français (1802-1885)." },
            ["Camus"] = new() { Prenom = "Albert", Nom = "Camus", Biographie = "Écrivain et philosophe, prix Nobel 1957." },
            ["Verne"] = new() { Prenom = "Jules", Nom = "Verne", Biographie = "Pionnier du roman d'aventures scientifiques." },
            ["Christie"] = new() { Prenom = "Agatha", Nom = "Christie", Biographie = "Reine du roman policier." },
            ["SaintEx"] = new() { Prenom = "Antoine", Nom = "de Saint-Exupéry", Biographie = "Aviateur et écrivain français." },
            ["Asimov"] = new() { Prenom = "Isaac", Nom = "Asimov", Biographie = "Auteur majeur de science-fiction." },
            ["Zola"] = new() { Prenom = "Émile", Nom = "Zola", Biographie = "Chef de file du naturalisme." },
            ["Voltaire"] = new() { Prenom = "", Nom = "Voltaire", Biographie = "Philosophe des Lumières." },
            ["Simenon"] = new() { Prenom = "Georges", Nom = "Simenon", Biographie = "Créateur du commissaire Maigret." },
            ["Mahfouz"] = new() { Prenom = "Naguib", Nom = "Mahfouz", Biographie = "Romancier égyptien, prix Nobel 1988." },
        };
        (string Titre, string Auteur, string Categorie, int Annee, int Exemplaires, string Resume)[] livres =
        [
            ("Les Misérables", "Hugo", "Roman", 1862, 3, "Jean Valjean, ancien forçat, cherche la rédemption dans la France du XIXe siècle."),
            ("Notre-Dame de Paris", "Hugo", "Roman", 1831, 2, "Quasimodo, Esmeralda et Frollo autour de la cathédrale."),
            ("Les Contemplations", "Hugo", "Philosophie", 1856, 1, "Recueil de poèmes sur l'amour, le deuil et la mort."),
            ("L'Étranger", "Camus", "Roman", 1942, 3, "Meursault, indifférent au monde, commet un meurtre sur une plage d'Alger."),
            ("La Peste", "Camus", "Roman", 1947, 2, "Une épidémie frappe Oran : chronique de la solidarité."),
            ("Le Mythe de Sisyphe", "Camus", "Philosophie", 1942, 1, "Essai sur l'absurde."),
            ("Vingt mille lieues sous les mers", "Verne", "Science-fiction", 1870, 2, "Le capitaine Nemo et le Nautilus."),
            ("De la Terre à la Lune", "Verne", "Science-fiction", 1865, 1, "Un canon géant pour atteindre la Lune."),
            ("Le Tour du monde en quatre-vingts jours", "Verne", "Jeunesse", 1872, 3, "Phileas Fogg parie qu'il fera le tour du monde en 80 jours."),
            ("Voyage au centre de la Terre", "Verne", "Science-fiction", 1864, 2, "Une descente dans un volcan islandais."),
            ("Le Crime de l'Orient-Express", "Christie", "Policier", 1934, 2, "Hercule Poirot enquête dans un train bloqué par la neige."),
            ("Dix petits nègres (Ils étaient dix)", "Christie", "Policier", 1939, 1, "Dix invités sur une île, un mystérieux assassin."),
            ("Mort sur le Nil", "Christie", "Policier", 1937, 2, "Un meurtre lors d'une croisière sur le Nil."),
            ("Le Meurtre de Roger Ackroyd", "Christie", "Policier", 1926, 1, "Le récit qui a bouleversé le roman policier."),
            ("Le Petit Prince", "SaintEx", "Jeunesse", 1943, 4, "Un aviateur rencontre un petit prince venu d'une autre planète."),
            ("Vol de nuit", "SaintEx", "Roman", 1931, 1, "Les pilotes de l'Aéropostale en Amérique du Sud."),
            ("Terre des hommes", "SaintEx", "Philosophie", 1939, 1, "Souvenirs d'aviateur et réflexion sur la condition humaine."),
            ("Fondation", "Asimov", "Science-fiction", 1951, 2, "Hari Seldon prévoit la chute de l'Empire galactique."),
            ("Les Robots", "Asimov", "Science-fiction", 1950, 2, "Nouvelles autour des trois lois de la robotique."),
            ("Les Cavernes d'acier", "Asimov", "Policier", 1954, 1, "Un détective et un robot enquêtent ensemble."),
            ("Germinal", "Zola", "Roman", 1885, 2, "La grève des mineurs du Nord."),
            ("L'Assommoir", "Zola", "Roman", 1877, 1, "La déchéance de Gervaise dans le Paris ouvrier."),
            ("Au Bonheur des Dames", "Zola", "Roman", 1883, 1, "La naissance des grands magasins."),
            ("Candide", "Voltaire", "Philosophie", 1759, 3, "Conte philosophique : tout est-il pour le mieux ?"),
            ("Zadig", "Voltaire", "Philosophie", 1747, 1, "Les épreuves d'un sage babylonien."),
            ("Pietr-le-Letton", "Simenon", "Policier", 1931, 1, "La première enquête du commissaire Maigret."),
            ("Maigret et le Corps sans tête", "Simenon", "Policier", 1955, 1, "Un corps repêché dans le canal Saint-Martin."),
            ("Le Chien jaune", "Simenon", "Policier", 1931, 2, "Maigret à Concarneau."),
            ("Impasse des deux palais", "Mahfouz", "Roman", 1956, 1, "Premier tome de la trilogie du Caire."),
            ("Les Fils de la médina", "Mahfouz", "Roman", 1959, 1, "Fable sur l'histoire spirituelle de l'humanité."),
        ];
        var n = 0;
        foreach (var l in livres)
        {
            n++;
            db.Livres.Add(new Livre
            {
                Titre = l.Titre,
                Isbn = Isbn13($"978207{n:000000}"),
                Resume = l.Resume,
                Annee = l.Annee,
                NbExemplaires = l.Exemplaires,
                NbDisponibles = l.Exemplaires,
                Auteur = auteurs[l.Auteur],
                Categorie = categories[l.Categorie],
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Quelques emprunts de démonstration : un en cours, un en retard (relances), un rendu (historique).</summary>
    private async Task EmpruntsDemoAsync(Utilisateur membre, CancellationToken ct)
    {
        var aujourdhui = horloge.Aujourdhui();
        var livres = await db.Livres.OrderBy(l => l.Id).ToListAsync(ct);
        Emprunt Pret(Livre l, int ilYaJours, int duree, bool rendu)
        {
            var debut = aujourdhui.AddDays(-ilYaJours);
            if (!rendu) l.NbDisponibles--;
            return new Emprunt
            {
                Livre = l, UtilisateurId = membre.Id, DateEmprunt = debut, DateRetourPrevue = debut.AddDays(duree),
                DateRetourReelle = rendu ? debut.AddDays(10) : null,
            };
        }
        db.Emprunts.AddRange(
            Pret(livres.First(l => l.Titre == "L'Étranger"), 5, 21, false),
            Pret(livres.First(l => l.Titre == "Fondation"), 24, 21, false),       // 3 jours de retard
            Pret(livres.First(l => l.Titre == "Le Petit Prince"), 60, 21, true)); // historique
        await db.SaveChangesAsync(ct);
    }

    private async Task DocumentsAsync(CancellationToken ct)
    {
        var maintenant = horloge.Maintenant();
        foreach (var (titre, type, contenu) in DocumentsConnaissance.Tous)
            db.Documents.Add(new DocumentConnaissance { Titre = titre, Type = type, Version = "1.0", Contenu = contenu, Statut = StatutPublication.Publie, PublieLe = maintenant });
        db.Documents.Add(new DocumentConnaissance
        {
            Titre = "Projet de tarifs 2027", Type = TypeDocument.Autre, Version = "0.1", Statut = StatutPublication.Brouillon,
            Contenu = "# Projet de tarifs 2027\n\n## Amendes\nBROUILLON : amende de 2 dinars par jour de retard (non validé).",
        });
        await db.SaveChangesAsync(ct);
        await indexeur.IndexerToutAsync(ct);
    }
}

/// <summary>Documents de connaissance initiaux (règlement, conditions, FAQ, horaires, charte WhatsApp).</summary>
public static class DocumentsConnaissance
{
    public static readonly (string Titre, TypeDocument Type, string Contenu)[] Tous =
    [
        ("Règlement intérieur", TypeDocument.Reglement, """
            # Règlement intérieur

            ## Inscription
            L'inscription est gratuite et se fait en ligne avec une adresse e-mail. Le mot de passe doit contenir au moins 8 caractères, une majuscule et un chiffre.
            Après 5 essais de mot de passe erronés, le compte est verrouillé pendant 15 minutes.

            ## Comportement
            Les livres doivent être rendus en bon état. Un livre perdu ou abîmé doit être remplacé ou remboursé au prix d'achat.

            ## Retards
            En cas de retard, un rappel est envoyé le lendemain de l'échéance. Après 7 jours de retard, une relance validée par un bibliothécaire est envoyée et le droit d'emprunter peut être suspendu jusqu'au retour du livre.
            """),
        ("Conditions de prêt", TypeDocument.ConditionsPret, """
            # Conditions de prêt

            ## Qui peut emprunter
            Tout membre inscrit peut emprunter. Le visiteur non inscrit peut consulter le catalogue et le règlement.

            ## Durée et nombre de livres
            La durée de prêt est de 21 jours. Un membre peut avoir 5 livres au maximum en même temps.
            Un livre ne peut être emprunté que si un exemplaire est disponible.

            ## Prolongation
            Un emprunt peut être prolongé une seule fois, de 7 jours, s'il n'est pas en retard et si personne n'a réservé le livre. La prolongation est validée par un bibliothécaire.

            ## Réservation
            Un livre indisponible peut être réservé. Au retour du livre, le premier de la file est prévenu et dispose de 3 jours pour venir le chercher ; passé ce délai, la réservation expire.

            ## Rappels
            Un rappel est envoyé 2 jours avant l'échéance, sur WhatsApp si le numéro est vérifié et le consentement donné, sinon par e-mail.
            """),
        ("Foire aux questions", TypeDocument.Faq, """
            # Foire aux questions

            ## Comment rendre un livre ?
            Rapportez-le à l'accueil : le bibliothécaire enregistre le retour dans MediaLib.

            ## Puis-je emprunter un livre déjà réservé par quelqu'un ?
            Non : quand un exemplaire revient, il est mis de côté pour le premier adhérent de la file d'attente.

            ## J'ai perdu un livre, que faire ?
            Signalez-le au bibliothécaire. Le livre doit être remplacé ou remboursé.

            ## Comment parler à un humain ?
            Écrivez « je veux parler à un bibliothécaire » à l'assistant : votre conversation lui est transmise.
            """),
        ("Horaires d'ouverture", TypeDocument.Horaires, """
            # Horaires d'ouverture

            ## Semaine
            La bibliothèque est ouverte du lundi au vendredi de 9 h à 18 h.

            ## Week-end
            Le samedi de 9 h à 13 h. Fermée le dimanche et les jours fériés.
            """),
        ("Charte WhatsApp", TypeDocument.CharteWhatsapp, """
            # Charte WhatsApp

            ## Consentement
            Les messages WhatsApp ne sont envoyés qu'après votre consentement explicite, donné dans « Mon profil », et la liaison de votre numéro avec un code à 6 chiffres.

            ## Arrêt des messages
            Répondez STOP à tout moment : votre consentement est retiré immédiatement et vous recevrez les rappels par e-mail.

            ## Fenêtre de 24 heures
            Hors des rappels automatiques (modèles approuvés), un bibliothécaire ne peut vous écrire librement que dans les 24 heures suivant votre dernier message.

            ## Données
            Votre numéro sert uniquement aux échanges avec la bibliothèque. Il n'est jamais partagé.
            """),
    ];
}
