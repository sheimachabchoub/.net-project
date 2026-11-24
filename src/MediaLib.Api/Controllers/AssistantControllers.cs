using MediaLib.Api.Agent;
using MediaLib.Api.Auth;
using MediaLib.Api.Communication;
using MediaLib.Api.Connaissance;
using MediaLib.Api.Data;
using MediaLib.Api.Domain;
using MediaLib.Api.Services;
using MediaLib.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Controllers;

/// <summary>Assistant IA (bibliothécaire virtuel) sur le canal Web.</summary>
[ApiController]
[Route("api/agent")]
[Produces("application/json")]
public class AgentController(
    MediaLibDbContext db,
    BibliothecaireIA agent,
    ServiceCommunication communication,
    DemandesAdherent demandes,
    UserManager<Utilisateur> users,
    TimeProvider horloge) : ControllerBase
{
    /// <summary>
    /// Pose une question à l'agent. Anonyme : catalogue et règlement seulement. Connecté : l'identité est celle du jeton,
    /// jamais un identifiant fourni dans la question.
    /// </summary>
    [HttpPost("messages")]
    [AllowAnonymous]
    [EnableRateLimiting("agent")]
    [ProducesResponseType<ReponseAgentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ReponseAgentDto> Poser(QuestionAgentRequete requete, CancellationToken ct)
    {
        var id = User.IdUtilisateurOuNull();
        var utilisateur = id is null ? null : await users.FindByIdAsync(id);
        var roles = utilisateur is null ? [] : (await users.GetRolesAsync(utilisateur)).ToList();

        Conversation? conversation = null;
        if (requete.ConversationId is { } cid)
        {
            conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == cid && c.Canal == Canal.Web && c.UtilisateurId == id, ct)
                ?? throw new IntrouvableException("Conversation introuvable.");
            if (conversation.Etat == EtatConversation.Close) conversation.Etat = EtatConversation.Agent;
        }
        if (conversation is null)
        {
            var maintenant = horloge.Maintenant();
            conversation = new Conversation { Canal = Canal.Web, UtilisateurId = id, OuverteLe = maintenant, DerniereActiviteLe = maintenant };
            db.Conversations.Add(conversation);
        }
        await communication.EnregistrerEntrantAsync(conversation, requete.Texte.Trim(), null, ct);
        return await agent.RepondreAsync(conversation, requete.Texte.Trim(), utilisateur, roles, "Assistant Web", ct);
    }

    /// <summary>Mes conversations avec l'assistant.</summary>
    [HttpGet("conversations")]
    [Authorize]
    [ProducesResponseType<List<ConversationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<ConversationDto>> MesConversations(CancellationToken ct)
    {
        var id = User.IdUtilisateur();
        var maintenant = horloge.Maintenant();
        var liste = await db.Conversations.Include(c => c.Utilisateur).Where(c => c.UtilisateurId == id)
            .OrderByDescending(c => c.DerniereActiviteLe).Select(c => new { c, n = c.Messages.Count }).ToListAsync(ct);
        return liste.Select(x => x.c.ToDto(maintenant, x.n)).ToList();
    }

    /// <summary>Messages d'une de mes conversations.</summary>
    [HttpGet("conversations/{id:int}")]
    [Authorize]
    [ProducesResponseType<ConversationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ConversationDetailDto> MaConversation(int id, CancellationToken ct)
    {
        var moi = User.IdUtilisateur();
        var c = await db.Conversations.Include(x => x.Utilisateur).Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == moi, ct) ?? throw new IntrouvableException("Conversation introuvable.");
        return new ConversationDetailDto(c.ToDto(horloge.Maintenant(), c.Messages.Count), c.Messages.OrderBy(m => m.CreeLe).ThenBy(m => m.Id).Select(m => m.ToDto()).ToList());
    }

    /// <summary>Confirme une réservation préparée par l'agent (l'adhérent décide).</summary>
    [HttpPost("propositions/{id:int}/confirmer")]
    [Authorize(Roles = Roles.Membre)]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ReservationDto> Confirmer(int id, CancellationToken ct)
    {
        var r = await demandes.ConfirmerReservationAsync(id, User.IdUtilisateur(), ct);
        var titre = await db.Livres.Where(l => l.Id == r.LivreId).Select(l => l.Titre).FirstAsync(ct);
        return new ReservationDto(r.Id, r.LivreId, titre, r.Statut, r.CreeLe, r.ExpireLe, 0);
    }
}

/// <summary>Suivi des conversations (Web, WhatsApp, e-mail) par les bibliothécaires : reprise humaine et réponses.</summary>
[ApiController]
[Route("api/gestion/conversations")]
[Authorize(Roles = Roles.Gestion)]
[Produces("application/json")]
public class ConversationsController(MediaLibDbContext db, ServiceCommunication communication, TimeProvider horloge) : ControllerBase
{
    /// <summary>Liste des conversations (filtres : canal, état).</summary>
    [HttpGet]
    [ProducesResponseType<List<ConversationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<ConversationDto>> Lister([FromQuery] Canal? canal, [FromQuery] EtatConversation? etat, CancellationToken ct)
    {
        var q = db.Conversations.Include(c => c.Utilisateur).AsQueryable();
        if (canal is { } ca) q = q.Where(c => c.Canal == ca);
        if (etat is { } et) q = q.Where(c => c.Etat == et);
        var maintenant = horloge.Maintenant();
        var liste = await q.OrderByDescending(c => c.DerniereActiviteLe).Take(200).Select(c => new { c, n = c.Messages.Count }).ToListAsync(ct);
        return liste.Select(x => x.c.ToDto(maintenant, x.n)).ToList();
    }

    /// <summary>Historique complet d'une conversation (entrants, sortants, statuts, erreurs, clés d'idempotence).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ConversationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ConversationDetailDto> Detail(int id, CancellationToken ct)
    {
        var c = await Charger(id, ct);
        return new ConversationDetailDto(c.ToDto(horloge.Maintenant(), c.Messages.Count), c.Messages.OrderBy(m => m.CreeLe).ThenBy(m => m.Id).Select(m => m.ToDto()).ToList());
    }

    /// <summary>Un bibliothécaire reprend la conversation : l'agent se tait.</summary>
    [HttpPost("{id:int}/reprendre")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Reprendre(int id, CancellationToken ct) => ChangerEtat(id, EtatConversation.Humain, ct);

    /// <summary>Rend la main à l'agent.</summary>
    [HttpPost("{id:int}/rendre-agent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RendreAgent(int id, CancellationToken ct) => ChangerEtat(id, EtatConversation.Agent, ct);

    /// <summary>Clôture la conversation.</summary>
    [HttpPost("{id:int}/cloturer")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Cloturer(int id, CancellationToken ct) => ChangerEtat(id, EtatConversation.Close, ct);

    /// <summary>Répond dans la conversation (WhatsApp : seulement dans la fenêtre de 24 h, sinon 409).</summary>
    [HttpPost("{id:int}/messages")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageDto>> Repondre(int id, ReponseBibliothecaireRequete requete, CancellationToken ct)
    {
        var c = await Charger(id, ct);
        try
        {
            var m = await communication.RepondreAsync(c, requete.Texte.Trim(), Message.AuteurBibliothecaire, ct);
            if (c.Etat == EtatConversation.Agent) c.Etat = EtatConversation.Humain;
            await db.SaveChangesAsync(ct);
            return Created($"/api/gestion/conversations/{id}", m.ToDto());
        }
        catch (EnvoiException e)
        {
            throw new RegleMetierException(e.Message);
        }
    }

    private async Task<IActionResult> ChangerEtat(int id, EtatConversation etat, CancellationToken ct)
    {
        var c = await Charger(id, ct);
        c.Etat = etat;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Conversation> Charger(int id, CancellationToken ct) =>
        await db.Conversations.Include(c => c.Utilisateur).Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new IntrouvableException("Conversation introuvable.");
}

/// <summary>Journal des décisions et validation humaine des actions sensibles.</summary>
[ApiController]
[Route("api/decisions")]
[Authorize(Roles = Roles.Gestion)]
[Produces("application/json")]
public class DecisionsController(MediaLibDbContext db, ValidationDecisions validation) : ControllerBase
{
    /// <summary>Journal des décisions, filtrable par statut (ex. EnAttenteValidation).</summary>
    [HttpGet]
    [ProducesResponseType<List<DecisionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<DecisionDto>> Lister([FromQuery] StatutDecision? statut, [FromQuery] int limite = 100, CancellationToken ct = default)
    {
        var q = db.Decisions.Include(d => d.Demandeur).Include(d => d.ValidePar).AsQueryable();
        if (statut is { } s) q = q.Where(d => d.Statut == s);
        var liste = await q.OrderByDescending(d => d.CreeLe).ThenByDescending(d => d.Id).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
        return liste.Select(d => d.ToDto()).ToList();
    }

    /// <summary>Détail d'une décision.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<DecisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DecisionDto> Detail(int id, CancellationToken ct) => (await validation.ChargerAsync(id, ct)).ToDto();

    /// <summary>Approuve puis exécute l'action (relance J+7, prolongation, message préparé…).</summary>
    [HttpPost("{id:int}/approuver")]
    [ProducesResponseType<DecisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DecisionDto> Approuver(int id, ValidationDecisionRequete requete, CancellationToken ct) =>
        (await validation.ApprouverAsync(id, User.IdUtilisateur(), requete.Commentaire, ct)).ToDto();

    /// <summary>Rejette l'action proposée.</summary>
    [HttpPost("{id:int}/rejeter")]
    [ProducesResponseType<DecisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DecisionDto> Rejeter(int id, ValidationDecisionRequete requete, CancellationToken ct) =>
        (await validation.RejeterAsync(id, User.IdUtilisateur(), requete.Commentaire, ct)).ToDto();
}

/// <summary>Documents de connaissance (RAG) : brouillon → publié → archivé.</summary>
[ApiController]
[Route("api/documents")]
[Authorize(Roles = Roles.Gestion)]
[Produces("application/json")]
public class DocumentsController(MediaLibDbContext db, IndexeurConnaissance indexeur, RechercheConnaissance recherche) : ControllerBase
{
    /// <summary>Tous les documents (tous statuts).</summary>
    [HttpGet]
    [ProducesResponseType<List<DocumentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<List<DocumentDto>> Lister(CancellationToken ct)
    {
        var liste = await db.Documents.OrderBy(d => d.Titre).ThenByDescending(d => d.Version).Select(d => new { d, n = d.Fragments.Count }).ToListAsync(ct);
        return liste.Select(x => x.d.ToDto(x.n)).ToList();
    }

    /// <summary>Détail d'un document.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DocumentDto> Detail(int id, CancellationToken ct)
    {
        var d = await Charger(id, ct);
        return d.ToDto(await db.Fragments.CountAsync(f => f.DocumentId == id, ct));
    }

    /// <summary>Recherche BM25 dans les documents publiés (publique), avec citation titre / version / section.</summary>
    [HttpGet("recherche")]
    [AllowAnonymous]
    [ProducesResponseType<List<ExtraitDto>>(StatusCodes.Status200OK)]
    public async Task<List<ExtraitDto>> Rechercher([FromQuery] string q, [FromQuery] int limite = 3, CancellationToken ct = default) =>
        await recherche.RechercherAsync(q ?? string.Empty, Math.Clamp(limite, 1, 10), ct);

    /// <summary>Crée un document en brouillon (non indexé).</summary>
    [HttpPost]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DocumentDto>> Creer(DocumentRequete r, CancellationToken ct)
    {
        var d = new DocumentConnaissance { Titre = r.Titre.Trim(), Type = r.Type, Version = r.Version, Contenu = r.Contenu };
        db.Documents.Add(d);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Detail), new { id = d.Id }, d.ToDto(0));
    }

    /// <summary>Modifie un document ; s'il est publié, il est réindexé.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DocumentDto> Modifier(int id, DocumentRequete r, CancellationToken ct)
    {
        var d = await Charger(id, ct);
        d.Titre = r.Titre.Trim();
        d.Type = r.Type;
        d.Version = r.Version;
        d.Contenu = r.Contenu;
        await db.SaveChangesAsync(ct);
        var n = await indexeur.IndexerAsync(d, ct);
        return d.ToDto(n);
    }

    /// <summary>Publie le document (les autres versions publiées du même titre sont archivées) et l'indexe.</summary>
    [HttpPost("{id:int}/publier")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DocumentDto> Publier(int id, CancellationToken ct)
    {
        var d = await Charger(id, ct);
        await indexeur.PublierAsync(d, ct);
        return d.ToDto(await db.Fragments.CountAsync(f => f.DocumentId == id, ct));
    }

    /// <summary>Archive le document : il sort de l'index.</summary>
    [HttpPost("{id:int}/archiver")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DocumentDto> Archiver(int id, CancellationToken ct)
    {
        var d = await Charger(id, ct);
        await indexeur.ArchiverAsync(d, ct);
        return d.ToDto(0);
    }

    /// <summary>Supprime un document et ses fragments.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        db.Documents.Remove(await Charger(id, ct));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<DocumentConnaissance> Charger(int id, CancellationToken ct) =>
        await db.Documents.FindAsync([id], ct) ?? throw new IntrouvableException("Document introuvable.");
}
