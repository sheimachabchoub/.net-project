using MediaLib.Shared;

namespace MediaLib.Api.Domain;

public class Emprunt
{
    public int Id { get; set; }
    public int LivreId { get; set; }
    public Livre Livre { get; set; } = null!;
    public string UtilisateurId { get; set; } = string.Empty;
    public Utilisateur Utilisateur { get; set; } = null!;
    public DateOnly DateEmprunt { get; set; }
    public DateOnly DateRetourPrevue { get; set; }
    public DateOnly? DateRetourReelle { get; set; }
    public int NbProlongations { get; set; }

    public bool EstEnCours => DateRetourReelle is null;

    public bool EstEnRetard(DateOnly aujourdhui) => EstEnCours && DateRetourPrevue < aujourdhui;

    public int JoursRetard(DateOnly aujourdhui) =>
        EstEnRetard(aujourdhui) ? aujourdhui.DayNumber - DateRetourPrevue.DayNumber : 0;

    public StatutEmprunt Statut(DateOnly aujourdhui, int joursEcheanceProche) => this switch
    {
        { EstEnCours: false } => StatutEmprunt.Rendu,
        _ when DateRetourPrevue < aujourdhui => StatutEmprunt.EnRetard,
        _ when DateRetourPrevue.DayNumber - aujourdhui.DayNumber <= joursEcheanceProche => StatutEmprunt.EcheanceProche,
        _ => StatutEmprunt.EnCours,
    };
}

public class Reservation
{
    public int Id { get; set; }
    public int LivreId { get; set; }
    public Livre Livre { get; set; } = null!;
    public string UtilisateurId { get; set; } = string.Empty;
    public Utilisateur Utilisateur { get; set; } = null!;
    public StatutReservation Statut { get; set; } = StatutReservation.EnAttente;
    public DateTime CreeLe { get; set; }

    /// <summary>Date à laquelle l'exemplaire a été mis de côté pour l'adhérent.</summary>
    public DateTime? NotifieeLe { get; set; }
    public DateTime? ExpireLe { get; set; }

    public bool EstActive => Statut is StatutReservation.EnAttente or StatutReservation.Notifiee;
}
