using MediaLib.Api.Domain;
using MediaLib.Shared;

namespace MediaLib.Api.Services;

/// <summary>Conversion entités → DTO : Blazor ne voit jamais les entités EF.</summary>
public static class Mapping
{
    public static LivreDto ToDto(this Livre l) => new(
        l.Id, l.Titre, l.Isbn, l.Resume, l.Annee, l.NbExemplaires, l.NbDisponibles, l.Couverture,
        l.AuteurId, l.Auteur?.NomComplet ?? string.Empty, l.CategorieId, l.Categorie?.Nom ?? string.Empty);

    public static EmpruntDto ToDto(this Emprunt e, DateOnly aujourdhui, int joursEcheanceProche) => new(
        e.Id, e.LivreId, e.Livre?.Titre ?? string.Empty, e.UtilisateurId, e.Utilisateur?.NomComplet ?? string.Empty,
        e.DateEmprunt, e.DateRetourPrevue, e.DateRetourReelle, e.NbProlongations,
        e.Statut(aujourdhui, joursEcheanceProche), e.JoursRetard(aujourdhui));

    public static UtilisateurDto ToDto(this Utilisateur u, IEnumerable<string> roles) => new(
        u.Id, u.Email ?? string.Empty, u.Prenom, u.Nom, roles.ToList(), u.Telephone, u.TelephoneVerifie,
        u.CanalPrefere, u.ConsentementWhatsapp);

    public static MessageDto ToDto(this Message m) => new(
        m.Id, m.Sens, m.Canal, m.Auteur, m.Contenu, m.Statut, m.CreeLe, m.Erreur, m.CleIdempotence);

    public static ConversationDto ToDto(this Conversation c, DateTime maintenant, int nbMessages) => new(
        c.Id, c.Canal, c.Etat, c.Utilisateur?.NomComplet, c.Telephone, c.OuverteLe, c.DerniereActiviteLe,
        c.FenetreOuverte(maintenant), nbMessages);

    public static DecisionDto ToDto(this DecisionAgent d) => new(
        d.Id, d.Declencheur, d.ActionProposee, d.ActionRetenue, d.Classement, d.Motif,
        JournalDecisions.Lire<List<string>>(d.OutilsJson) ?? [],
        JournalDecisions.Lire<List<SourceDto>>(d.SourcesJson) ?? [],
        d.DonneesJson, d.Statut, d.Resultat, d.ValidePar?.NomComplet, d.Demandeur?.NomComplet,
        d.EmpruntId, d.ConversationId, d.CreeLe, d.TraiteLe);

    public static DocumentDto ToDto(this DocumentConnaissance d, int nbFragments) => new(
        d.Id, d.Titre, d.Type, d.Version, d.Contenu, d.Statut, d.PublieLe, d.IndexeLe, nbFragments);

    public static RelanceDto ToDto(this Relance r) => new(
        r.Id, r.EmpruntId, r.Emprunt?.Livre?.Titre ?? string.Empty, r.Emprunt?.Utilisateur?.NomComplet ?? string.Empty,
        r.Niveau, r.Echeance, r.Statut, r.Tentatives, r.CleIdempotence, r.PrevueLe, r.DecisionId);
}
