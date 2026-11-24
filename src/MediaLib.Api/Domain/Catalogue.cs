namespace MediaLib.Api.Domain;

public class Auteur
{
    public int Id { get; set; }
    public string Prenom { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string? Biographie { get; set; }
    public List<Livre> Livres { get; set; } = [];

    public string NomComplet => $"{Prenom} {Nom}".Trim();
}

public class Categorie
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Livre> Livres { get; set; } = [];
}

public class Livre
{
    public int Id { get; set; }
    public string Titre { get; set; } = string.Empty;

    /// <summary>ISBN normalisé (chiffres seulement, X final possible), unique.</summary>
    public string Isbn { get; set; } = string.Empty;
    public string? Resume { get; set; }
    public int Annee { get; set; }
    public int NbExemplaires { get; set; }

    /// <summary>Exemplaires physiquement en rayon (décrémenté au prêt, incrémenté au retour).</summary>
    public int NbDisponibles { get; set; }
    public string? Couverture { get; set; }

    public int AuteurId { get; set; }
    public Auteur Auteur { get; set; } = null!;
    public int CategorieId { get; set; }
    public Categorie Categorie { get; set; } = null!;

    /// <summary>Jeton de concurrence optimiste : deux prêts simultanés du dernier exemplaire ne passent pas tous les deux.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public List<Emprunt> Emprunts { get; set; } = [];
}
