using MediaLib.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediaLib.Api.Data;

public class MediaLibDbContext(DbContextOptions<MediaLibDbContext> options)
    : IdentityDbContext<Utilisateur, IdentityRole, string>(options)
{
    public DbSet<Auteur> Auteurs => Set<Auteur>();
    public DbSet<Categorie> Categories => Set<Categorie>();
    public DbSet<Livre> Livres => Set<Livre>();
    public DbSet<Emprunt> Emprunts => Set<Emprunt>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DocumentConnaissance> Documents => Set<DocumentConnaissance>();
    public DbSet<FragmentConnaissance> Fragments => Set<FragmentConnaissance>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Relance> Relances => Set<Relance>();
    public DbSet<DecisionAgent> Decisions => Set<DecisionAgent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Tous les enums sont stockés en texte : la base reste lisible.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Utilisateur>(e =>
        {
            e.Property(u => u.Prenom).HasMaxLength(100);
            e.Property(u => u.Nom).HasMaxLength(100);
            e.Property(u => u.Telephone).HasMaxLength(20);
            e.Property(u => u.CodeLiaison).HasMaxLength(6);
            e.HasIndex(u => u.Telephone).IsUnique().HasFilter("Telephone IS NOT NULL");
            e.HasIndex(u => u.CodeLiaison);
        });

        b.Entity<Auteur>(e =>
        {
            e.Property(a => a.Prenom).HasMaxLength(100);
            e.Property(a => a.Nom).HasMaxLength(100).IsRequired();
        });

        b.Entity<Categorie>(e =>
        {
            e.Property(c => c.Nom).HasMaxLength(100).IsRequired();
            e.HasIndex(c => c.Nom).IsUnique();
        });

        b.Entity<Livre>(e =>
        {
            e.Property(l => l.Titre).HasMaxLength(200).IsRequired();
            e.Property(l => l.Isbn).HasMaxLength(13).IsRequired();
            e.HasIndex(l => l.Isbn).IsUnique();
            e.HasIndex(l => l.Titre);
            e.Property(l => l.Couverture).HasMaxLength(500);
            e.Property(l => l.Version).IsConcurrencyToken();
            // Restrict : un auteur ou une catégorie liés à un livre ne peuvent pas être supprimés (409 côté API).
            e.HasOne(l => l.Auteur).WithMany(a => a.Livres).HasForeignKey(l => l.AuteurId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Categorie).WithMany(c => c.Livres).HasForeignKey(l => l.CategorieId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Livre_Disponibles", "NbDisponibles >= 0 AND NbDisponibles <= NbExemplaires");
            });
        });

        b.Entity<Emprunt>(e =>
        {
            e.HasOne(x => x.Livre).WithMany(l => l.Emprunts).HasForeignKey(x => x.LivreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Utilisateur).WithMany(u => u.Emprunts).HasForeignKey(x => x.UtilisateurId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UtilisateurId, x.DateRetourReelle });
            e.HasIndex(x => x.DateRetourPrevue);
        });

        b.Entity<Reservation>(e =>
        {
            e.HasOne(x => x.Livre).WithMany().HasForeignKey(x => x.LivreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Utilisateur).WithMany().HasForeignKey(x => x.UtilisateurId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.LivreId, x.Statut, x.CreeLe });
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(r => r.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(r => r.TokenHash).IsUnique();
            e.HasOne(r => r.Utilisateur).WithMany().HasForeignKey(r => r.UtilisateurId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentConnaissance>(e =>
        {
            e.Property(d => d.Titre).HasMaxLength(200).IsRequired();
            e.Property(d => d.Version).HasMaxLength(20);
            e.HasMany(d => d.Fragments).WithOne(f => f.Document).HasForeignKey(f => f.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FragmentConnaissance>(e =>
        {
            e.Property(f => f.Section).HasMaxLength(300);
            e.HasIndex(f => new { f.DocumentId, f.Rang });
        });

        b.Entity<Conversation>(e =>
        {
            e.Property(c => c.Telephone).HasMaxLength(20);
            e.HasOne(c => c.Utilisateur).WithMany().HasForeignKey(c => c.UtilisateurId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(c => c.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.Canal, c.UtilisateurId });
            e.HasIndex(c => new { c.Canal, c.Telephone });
        });

        b.Entity<Message>(e =>
        {
            e.Property(m => m.Auteur).HasMaxLength(30);
            e.Property(m => m.CleIdempotence).HasMaxLength(120);
            e.Property(m => m.IdentifiantExterne).HasMaxLength(200);
            // Idempotence : une clé ne peut produire qu'un seul message.
            e.HasIndex(m => m.CleIdempotence).IsUnique().HasFilter("CleIdempotence IS NOT NULL");
            e.HasIndex(m => m.IdentifiantExterne).IsUnique().HasFilter("IdentifiantExterne IS NOT NULL");
        });

        b.Entity<Relance>(e =>
        {
            e.Property(r => r.CleIdempotence).HasMaxLength(120).IsRequired();
            e.HasIndex(r => r.CleIdempotence).IsUnique();
            // Pas de doublon : une relance par emprunt, niveau et échéance.
            e.HasIndex(r => new { r.EmpruntId, r.Niveau, r.Echeance }).IsUnique();
            e.HasOne(r => r.Emprunt).WithMany().HasForeignKey(r => r.EmpruntId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Message).WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.Decision).WithMany().HasForeignKey(r => r.DecisionId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DecisionAgent>(e =>
        {
            e.Property(d => d.Declencheur).HasMaxLength(200);
            e.Property(d => d.ActionProposee).HasMaxLength(80);
            e.Property(d => d.ActionRetenue).HasMaxLength(80);
            e.HasIndex(d => new { d.Statut, d.CreeLe });
            e.HasOne(d => d.Demandeur).WithMany().HasForeignKey(d => d.DemandeurId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.ValidePar).WithMany().HasForeignKey(d => d.ValideParId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Emprunt).WithMany().HasForeignKey(d => d.EmpruntId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Conversation).WithMany().HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Message).WithMany().HasForeignKey(d => d.MessageId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
