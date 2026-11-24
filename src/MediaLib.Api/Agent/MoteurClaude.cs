using System.Text.Json;
using Anthropic;
using MediaLib.Api.Configuration;
using MediaLib.Api.Services;
using Microsoft.Extensions.Options;
using A = Anthropic.Models.Messages;

namespace MediaLib.Api.Agent;

/// <summary>Le modèle a refusé de répondre (stop_reason « refusal ») : le moteur simulé prend le relais.</summary>
public class RefusModeleException(string message) : Exception(message);

/// <summary>
/// Moteur réel : API Claude (SDK officiel Anthropic) avec appel d'outils.
/// Le modèle ne reçoit jamais d'identifiant d'adhérent : l'identité est dans le contexte serveur.
/// </summary>
public class MoteurClaude(
    OutilsAgent outils,
    ContexteDemandeur contexte,
    IOptions<AgentOptions> options,
    TimeProvider horloge,
    IWebHostEnvironment env) : IMoteurAgent
{
    private const int ToursMax = 6;

    private static readonly List<A.ToolUnion> Outils =
    [
        Outil("rechercherLivres", "Cherche des livres du catalogue par titre, auteur ou thème. Seule source fiable pour citer un livre.",
            new() { ["texte"] = P("string", "Mots du titre, de l'auteur ou du thème"), ["categorie"] = P("string", "Catégorie (facultatif)"), ["disponibleSeulement"] = P("boolean", "Seulement les livres disponibles") },
            ["texte"]),
        Outil("voirDisponibilite", "Disponibilité d'un livre (exemplaires libres, prochain retour, file de réservation).",
            new() { ["livreId"] = P("integer", "Identifiant du livre renvoyé par rechercherLivres") }, ["livreId"]),
        Outil("recommanderLivres", "Recommande des livres proches d'un livre du catalogue ou d'un thème.",
            new() { ["livreId"] = P("integer", "Livre de référence (facultatif)"), ["texte"] = P("string", "Thème ou genre (facultatif)") }, []),
        Outil("chercherReglement", "Cherche dans les documents publiés (règlement, conditions de prêt, FAQ, horaires, charte WhatsApp). Toujours citer la source.",
            new() { ["question"] = P("string", "La question sur le règlement") }, ["question"]),
        Outil("voirMesEmprunts", "Emprunts et réservations du demandeur connecté (identité fournie par le serveur).", new(), []),
        Outil("preparerReservation", "Prépare une réservation pour le demandeur connecté ; il devra la confirmer.",
            new() { ["livreId"] = P("integer", "Identifiant du livre") }, ["livreId"]),
        Outil("preparerProlongation", "Prépare une demande de prolongation d'un emprunt du demandeur ; un bibliothécaire doit la valider.",
            new() { ["empruntId"] = P("integer", "Identifiant de l'emprunt renvoyé par voirMesEmprunts") }, ["empruntId"]),
        Outil("preparerRelance", "Bibliothécaire uniquement : prépare une relance pour un emprunt ; elle sera envoyée après validation humaine.",
            new() { ["empruntId"] = P("integer", "Identifiant de l'emprunt"), ["texte"] = P("string", "Texte proposé (facultatif)") }, ["empruntId"]),
        Outil("transmettreAuBibliothecaire", "Transmet la conversation à un bibliothécaire humain (contestation, cas non prévu, demande explicite).",
            new() { ["motif"] = P("string", "Motif court"), ["resume"] = P("string", "Résumé de la demande") }, ["motif", "resume"]),
        Outil("resumerAdherent", "Résumé de situation : le sien pour un membre ; pour un bibliothécaire, un adhérent (par e-mail) ou la situation du jour.",
            new() { ["email"] = P("string", "E-mail de l'adhérent (bibliothécaire uniquement, facultatif)") }, []),
    ];

    public async Task<string> RepondreAsync(string question, IReadOnlyList<EchangeHistorique> historique, CancellationToken ct)
    {
        var o = options.Value;
        var client = new AnthropicClient { ApiKey = o.CleApi, Timeout = TimeSpan.FromSeconds(o.DelaiSecondes), MaxRetries = 1 };

        List<A.MessageParam> messages = [];
        foreach (var e in historique.TakeLast(10))
        {
            if (e.DeLAdherent) messages.Add(new A.UserMessageParam { Content = e.Texte });
            else messages.Add(new A.AssistantMessageParam { Content = new List<A.ContentBlockParam> { new A.TextBlockParam { Text = e.Texte } } });
        }
        // L'historique doit commencer par un message utilisateur.
        while (messages.Count > 0 && messages[0].TryPickAssistant(out _)) messages.RemoveAt(0);
        messages.Add(new A.UserMessageParam { Content = question });

        for (var tour = 0; tour < ToursMax; tour++)
        {
            var reponse = await client.Messages.Create(new A.MessageCreateParams
            {
                Model = o.Modele,
                MaxTokens = o.MaxTokens,
                System = new List<A.TextBlockParam> { new() { Text = await ConsigneAsync(ct) } },
                Tools = Outils,
                Messages = messages,
                OutputConfig = new A.OutputConfig { Effort = A.Effort.Medium },
            }, cancellationToken: ct);

            if (reponse.StopReason == "refusal") throw new RefusModeleException("Le modèle a décliné la demande.");

            List<A.ContentBlockParam> assistant = [];
            List<A.ContentBlockParam> resultats = [];
            var texte = new List<string>();
            foreach (var bloc in reponse.Content)
            {
                if (bloc.TryPickText(out var t))
                {
                    assistant.Add(new A.TextBlockParam { Text = t.Text });
                    texte.Add(t.Text);
                }
                else if (bloc.TryPickThinking(out var th))
                    assistant.Add(new A.ThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                else if (bloc.TryPickRedactedThinking(out var rt))
                    assistant.Add(new A.RedactedThinkingBlockParam { Data = rt.Data });
                else if (bloc.TryPickToolUse(out var outil))
                {
                    assistant.Add(new A.ToolUseBlockParam { ID = outil.ID, Name = outil.Name, Input = outil.Input });
                    var entree = JsonSerializer.SerializeToElement(outil.Input);
                    string resultat;
                    try
                    {
                        resultat = await outils.ExecuterAsync(outil.Name, entree, ct);
                    }
                    catch (Exception ex) when (ex is RegleMetierException or IntrouvableException)
                    {
                        resultat = JsonSerializer.Serialize(new { erreur = ex.Message });
                    }
                    resultats.Add(new A.ToolResultBlockParam { ToolUseID = outil.ID, Content = resultat });
                }
            }

            if (resultats.Count == 0) return string.Join("\n", texte).Trim();
            messages.Add(new A.AssistantMessageParam { Content = assistant });
            messages.Add(new A.UserMessageParam { Content = resultats });
        }
        return "Je n'ai pas pu terminer ma recherche. Reformulez votre question ou demandez un bibliothécaire.";
    }

    private async Task<string> ConsigneAsync(CancellationToken ct)
    {
        var chemin = Path.Combine(env.ContentRootPath, "Agent", "consigne_bibliothecaire.md");
        var consigne = File.Exists(chemin) ? await File.ReadAllTextAsync(chemin, ct) : "Tu es le bibliothécaire IA de MediaLib. Réponds en français.";
        var u = contexte.Utilisateur;
        var qui = u is null ? "un visiteur anonyme : seuls le catalogue et le règlement lui sont accessibles"
            : contexte.EstBibliothecaire ? $"le bibliothécaire {u.NomComplet}"
            : $"l'adhérent identifié {u.Prenom}";
        return consigne + $"\n\n## Contexte serveur (fiable)\n- Date du jour : {horloge.Aujourdhui():dd/MM/yyyy}\n- Canal : {contexte.Canal}\n- Demandeur : {qui}\n";
    }

    private static A.Tool Outil(string nom, string description, Dictionary<string, JsonElement> proprietes, string[] requis) => new()
    {
        Name = nom,
        Description = description,
        InputSchema = new() { Properties = proprietes, Required = requis },
    };

    private static JsonElement P(string type, string description) => JsonSerializer.SerializeToElement(new { type, description });
}
