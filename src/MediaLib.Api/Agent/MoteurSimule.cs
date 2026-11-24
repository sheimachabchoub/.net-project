using System.Text.Json;
using System.Text.RegularExpressions;
using MediaLib.Api.Connaissance;
using MediaLib.Api.Services;
using MediaLib.Shared;

namespace MediaLib.Api.Agent;

public record EchangeHistorique(bool DeLAdherent, string Texte);

public interface IMoteurAgent
{
    Task<string> RepondreAsync(string question, IReadOnlyList<EchangeHistorique> historique, CancellationToken ct);
}

/// <summary>
/// Moteur simulé hors ligne : reconnaît l'intention par mots-clés et appelle les mêmes outils que Claude.
/// Il ne invente jamais : sans résultat d'outil, il le dit.
/// </summary>
public class MoteurSimule(OutilsAgent outils, ContexteDemandeur contexte, EmpruntService emprunts) : IMoteurAgent
{
    private static readonly string[] MotsReservation = ["reserver", "reservation", "reserve", "voudrais", "veux", "aimerais", "pouvez", "pourriez", "livre"];
    private static readonly string[] MotsRecommandation = ["recommande", "recommander", "recommandez", "recommandation", "similaire", "similaires", "conseille", "conseiller", "conseillez", "suggere", "suggerer", "genre", "meme", "auteur", "comme", "livre", "livres", "aime"];

    public async Task<string> RepondreAsync(string question, IReadOnlyList<EchangeHistorique> historique, CancellationToken ct)
    {
        var q = Normaliseur.SansAccents(question);
        if (string.IsNullOrWhiteSpace(q)) return "Posez-moi une question sur le catalogue, vos emprunts ou le règlement.";
        if (C(q, @"\b(deja (rendu|ramene|rapporte)|je l ai rendu|conteste|contestation|reclamation|plainte|parler (a|avec) (un|une|le|la) (humain|personne|bibliothecaire)|erreur sur mon compte)\b"))
            return await EscaladerAsync("contestation", question, ct);
        // Un bibliothécaire peut demander la situation du jour ou celle d'un adhérent (par e-mail).
        if (contexte.EstBibliothecaire && C(q, @"\b(resume|resumer|situation du jour|priorit)"))
            return await outils.ResumerAdherentAsync(ExtraireEmail(question), ct);
        if (C(q, @"\b(emprunts?|prets?|retards?|reservations?|compte|situation)\s+(de|d|du)\s+(?!moi\b|mon\b|ma\b|mes\b|jour\b)[a-z]"))
            return RefuserAutrui();
        if (C(q, @"^(bonjour|salut|bonsoir|hello|aide|help|\?)\W*$")) return Presentation();
        if (contexte.EstBibliothecaire && C(q, @"\b(prepare|preparer|rediger)\b.*\brelance") && ExtraireNombre(q) is { } empruntId)
            return await PreparerRelanceAsync(empruntId, ct);
        if (C(q, @"\bprolong") && C(q, @"\b(mon|ma|mes|le livre|demande|voudrais|veux|pouvez|peux|pourriez|prolonger)\b"))
            return await ProlongerAsync(question, ct);
        if (C(q, @"\breserv") && !C(q, @"\b(combien|regle|reglement|comment|delai)\b"))
            return await ReserverAsync(question, ct);
        if (C(q, @"\b(mes (emprunts|prets|livres)|ma situation|quand (dois|faut)|(dois|faut) je rendre|a rendre|rendre mes|mon echeance|mes echeances|suis je en retard)\b"))
            return await MesEmpruntsAsync(ct);
        if (C(q, @"\b(combien de (jours|temps|semaines|livres)|regle|reglement|horaire|ouvert|ouverture|ferme|duree|garder|penalit|amende|whatsapp|stop|inscri|perdu|abime|maximum|retard|conditions?|charte|carte)"))
            return await ReglementAsync(question, ct);
        if (C(q, @"\b(recommand|similaire|conseill|sugger|dans le meme genre|du meme auteur)"))
            return await RecommanderAsync(question, ct);
        return await RechercherAsync(question, ct);
    }

    private string Presentation()
    {
        var qui = contexte.Utilisateur?.Prenom;
        return (qui is null ? "Bonjour ! " : $"Bonjour {qui} ! ") + "Je suis le Bibliothécaire IA. Je peux :\n"
            + "- chercher un livre du catalogue et vous dire s'il est disponible ;\n"
            + "- répondre aux questions sur le règlement, en citant le document ;\n"
            + (qui is null ? "- après connexion, vous rappeler vos emprunts ;\n" : "- vous rappeler vos emprunts et préparer une prolongation ou une réservation ;\n")
            + "- transmettre votre demande à un bibliothécaire.";
    }

    private string RefuserAutrui() =>
        "Je ne peux communiquer aucune information sur le compte d'une autre personne. "
        + (contexte.EstIdentifie ? "Je peux en revanche vous montrer vos propres emprunts : demandez « mes emprunts »." : "Connectez-vous pour consulter vos propres emprunts.");

    private async Task<string> EscaladerAsync(string motif, string question, CancellationToken ct)
    {
        await outils.TransmettreAuBibliothecaireAsync(motif, question, ct);
        return "Je comprends. J'ai transmis votre message à un bibliothécaire, qui va vérifier votre dossier et vous répondre ici.";
    }

    private async Task<string> MesEmpruntsAsync(CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToElement(await outils.VoirMesEmpruntsAsync(ct), MediaLibJson.Options);
        if (json.TryGetProperty("erreur", out _))
            return contexte.Canal == Canal.WhatsApp
                ? "Votre numéro n'est pas encore lié à un compte. Connectez-vous sur le site, rubrique « Mon profil », pour lier votre WhatsApp."
                : "Pour consulter vos emprunts, connectez-vous d'abord. Je ne donne aucune information personnelle sans connexion.";
        var adherent = json.GetProperty("adherent").GetString();
        var liste = json.GetProperty("empruntsEnCours").EnumerateArray().ToList();
        string texte;
        if (liste.Count == 0)
            texte = $"Vous n'avez aucun emprunt en cours, {adherent}. Vous pouvez emprunter jusqu'à {json.GetProperty("empruntsRestantsAutorises").GetInt32()} livres.";
        else
        {
            var lignes = new List<string> { $"Voici vos emprunts en cours, {adherent} :" };
            foreach (var e in liste)
            {
                var j = e.GetProperty("joursRestants").GetInt32();
                var quand = j < 0 ? $"en retard de {-j} jour(s)" : j == 0 ? "à rendre aujourd'hui" : $"dans {j} jour(s)";
                lignes.Add($"- « {e.GetProperty("livre").GetString()} » : à rendre le {e.GetProperty("aRendreLe").GetString()} ({quand}).");
            }
            texte = string.Join('\n', lignes);
        }
        foreach (var r in json.GetProperty("reservations").EnumerateArray())
            texte += $"\nRéservation : « {r.GetProperty("livre").GetString()} » — {r.GetProperty("statut").GetString()}"
                     + (r.GetProperty("aRetirerAvantLe").ValueKind == JsonValueKind.String ? $", à retirer avant le {r.GetProperty("aRetirerAvantLe").GetString()}" : string.Empty) + ".";
        return texte;
    }

    private async Task<string> ProlongerAsync(string question, CancellationToken ct)
    {
        if (contexte.Utilisateur is not { } u) return "Connectez-vous pour demander une prolongation : je dois savoir quel emprunt prolonger.";
        var enCours = await emprunts.MesEmpruntsAsync(u.Id, false, ct);
        if (enCours.Count == 0) return "Vous n'avez aucun emprunt en cours à prolonger.";
        var mots = Normaliseur.MotsSignificatifs(question).Except(["prolonger", "prolongation", "voudrais", "veux", "emprunt", "livre"]).ToList();
        var choisis = enCours.Where(e => mots.Any(m => Normaliseur.SansAccents(e.Livre.Titre).Contains(m))).ToList();
        if (choisis.Count != 1)
        {
            if (enCours.Count == 1) choisis = enCours;
            else return "Quel livre souhaitez-vous prolonger ?\n" + string.Join('\n', enCours.Select(e => $"- « {e.Livre.Titre} »"));
        }
        var emprunt = choisis[0];
        var r = JsonSerializer.SerializeToElement(await outils.PreparerProlongationAsync(emprunt.Id, ct), MediaLibJson.Options);
        if (!r.GetProperty("possible").GetBoolean())
            return $"La prolongation de « {emprunt.Livre.Titre} » n'est pas possible : {Minuscule(r.GetProperty("motif").GetString())}";
        if (r.GetProperty("dejaDemandee").GetBoolean())
            return $"Votre demande de prolongation pour « {emprunt.Livre.Titre} » est déjà en attente de validation par le bibliothécaire.";
        return $"J'ai préparé votre demande de prolongation pour « {emprunt.Livre.Titre} ». Elle doit être validée par le bibliothécaire ; si elle est acceptée, le livre sera à rendre le {r.GetProperty("nouvelleEcheanceSiAcceptee").GetString()}.";
    }

    private async Task<string> ReserverAsync(string question, CancellationToken ct)
    {
        if (!contexte.EstIdentifie) return "Connectez-vous pour réserver un livre.";
        var resultat = await outils.RechercherLivresAsync(SansMots(question, MotsReservation), ct: ct);
        if (resultat.Total != 1)
            return resultat.Total == 0
                ? "Je ne trouve pas ce livre au catalogue. Précisez son titre ?"
                : "Plusieurs livres correspondent. Lequel voulez-vous réserver ?\n" + Liste(resultat.Livres);
        var livre = resultat.Livres[0];
        var r = JsonSerializer.SerializeToElement(await outils.PreparerReservationAsync(livre.Id, ct), MediaLibJson.Options);
        if (!r.GetProperty("possible").GetBoolean())
            return $"Réservation impossible pour « {livre.Titre} » : {Minuscule(r.GetProperty("motif").GetString())}";
        var n = r.GetProperty("personnesDejaEnAttente").GetInt32();
        var attente = n > 0 ? $" {n} personne(s) attendent déjà ce livre." : string.Empty;
        return contexte.Canal == Canal.WhatsApp
            ? $"« {livre.Titre} » n'est pas disponible.{attente} Répondez OUI pour confirmer la réservation."
            : $"« {livre.Titre} » n'est pas disponible.{attente} Confirmez la réservation avec le bouton ci-dessous.";
    }

    private async Task<string> ReglementAsync(string question, CancellationToken ct)
    {
        var extraits = await outils.ChercherReglementAsync(question, ct);
        if (extraits.Count == 0)
            return "Je ne trouve pas de règle publiée sur ce point. Je préfère ne pas inventer : un bibliothécaire pourra vous répondre.";
        var e = extraits[0];
        var texte = Regex.Replace(e.Contenu, @"\s+", " ").Trim();
        return $"{texte}\n\nSource : {e.Titre}, version {e.Version}, section « {e.Section} ».";
    }

    private async Task<string> RecommanderAsync(string question, CancellationToken ct)
    {
        var mots = SansMots(question, MotsRecommandation);
        var reference = mots.Length > 0 ? await outils.RechercherLivresAsync(mots, ct: ct) : new ResultatRecherche(0, []);
        var r = reference.Total > 0
            ? await outils.RecommanderLivresAsync(reference.Livres[0].Id, ct: ct)
            : await outils.RecommanderLivresAsync(null, mots, ct);
        if (r.Livres.Count == 0) return "Je n'ai pas de livre proche à vous proposer dans notre catalogue.";
        var intro = reference.Total > 0 ? $"Si vous avez aimé « {reference.Livres[0].Titre} », voici des livres du catalogue proches :" : "Voici quelques livres du catalogue :";
        return intro + "\n" + Liste(r.Livres);
    }

    private async Task<string> RechercherAsync(string question, CancellationToken ct)
    {
        var disponible = C(Normaliseur.SansAccents(question), @"\bdisponible");
        var r = await outils.RechercherLivresAsync(question, null, disponible, ct);
        if (r.Total == 0)
        {
            if (r.Message is not null) return "Des livres correspondent, mais aucun exemplaire n'est disponible pour le moment. Je peux vous aider à en réserver un.";
            var chercheUnLivre = C(Normaliseur.SansAccents(question), @"\b(livres?|romans?|ouvrages?|auteurs?|ecrits? par|avez vous|titre|tome)\b");
            if (!chercheUnLivre && Normaliseur.Termes(question).Count <= 6 && (await outils.ChercherReglementAsync(question, ct)).Count > 0)
                return await ReglementAsync(question, ct);
            if (contexte.Canal == Canal.WhatsApp && Normaliseur.MotsSignificatifs(question).Count > 3)
                return await EscaladerAsync("incomprehension", question, ct);
            return "Ce livre n'est pas présent dans notre catalogue, et je préfère ne rien inventer. Essayez un autre titre, un auteur ou une catégorie.";
        }
        var intro = disponible ? $"J'ai trouvé {r.Total} livre(s) disponible(s) au catalogue :" : $"J'ai trouvé {r.Total} livre(s) au catalogue :";
        return intro + "\n" + Liste(r.Livres);
    }

    private async Task<string> PreparerRelanceAsync(int empruntId, CancellationToken ct)
    {
        var r = JsonSerializer.SerializeToElement(await outils.PreparerRelanceAsync(empruntId, null, ct), MediaLibJson.Options);
        return r.GetProperty("possible").GetBoolean()
            ? $"Relance préparée pour l'emprunt n°{empruntId}. Elle attend votre validation dans « Décisions à valider » :\n{r.GetProperty("texte").GetString()}"
            : "Relance impossible : " + r.GetProperty("motif").GetString();
    }

    private static string Liste(IEnumerable<LivreTrouve> livres) => string.Join('\n', livres.Select(l =>
        $"- « {l.Titre} » de {l.Auteur} ({l.Annee}) — {(l.Disponibles > 0 ? $"{l.Disponibles} exemplaire(s) disponible(s)" : "indisponible pour le moment")}"));

    private static string SansMots(string texte, IEnumerable<string> mots) =>
        string.Join(' ', Normaliseur.MotsSignificatifs(texte).Except(mots));

    private static bool C(string texte, string motif) => Regex.IsMatch(texte, motif);

    private static string Minuscule(string? s) => string.IsNullOrEmpty(s) ? string.Empty : char.ToLowerInvariant(s[0]) + s[1..];

    private static int? ExtraireNombre(string q) => Regex.Match(q, @"\b(\d{1,9})\b") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    private static string? ExtraireEmail(string q) => Regex.Match(q, @"[\w.+-]+@[\w-]+(\.[\w-]+)+") is { Success: true } m ? m.Value : null;
}
