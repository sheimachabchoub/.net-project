using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaLib.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Prenom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InscritLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    TelephoneVerifieLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CanalPrefere = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentementWhatsappLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CodeLiaison = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true),
                    CodeLiaisonExpireLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Auteurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Prenom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Biographie = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auteurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Titre = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Contenu = table.Column<string>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PublieLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IndexeLe = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Canal = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Etat = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UtilisateurId = table.Column<string>(type: "TEXT", nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    OuverteLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DernierMessageEntrantLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DerniereActiviteLe = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_AspNetUsers_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UtilisateurId = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreeLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpireLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevoqueLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemplaceParHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AspNetUsers_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Livres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Titre = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Isbn = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    Resume = table.Column<string>(type: "TEXT", nullable: true),
                    Annee = table.Column<int>(type: "INTEGER", nullable: false),
                    NbExemplaires = table.Column<int>(type: "INTEGER", nullable: false),
                    NbDisponibles = table.Column<int>(type: "INTEGER", nullable: false),
                    Couverture = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategorieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Livres", x => x.Id);
                    table.CheckConstraint("CK_Livre_Disponibles", "NbDisponibles >= 0 AND NbDisponibles <= NbExemplaires");
                    table.ForeignKey(
                        name: "FK_Livres_Auteurs_AuteurId",
                        column: x => x.AuteurId,
                        principalTable: "Auteurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Livres_Categories_CategorieId",
                        column: x => x.CategorieId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Contenu = table.Column<string>(type: "TEXT", nullable: false),
                    Termes = table.Column<string>(type: "TEXT", nullable: false),
                    Rang = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fragments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sens = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Canal = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Auteur = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Contenu = table.Column<string>(type: "TEXT", nullable: false),
                    Modele = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifiantExterne = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CleIdempotence = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreeLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnvoyeLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StatutMisAJourLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Erreur = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Emprunts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LivreId = table.Column<int>(type: "INTEGER", nullable: false),
                    UtilisateurId = table.Column<string>(type: "TEXT", nullable: false),
                    DateEmprunt = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DateRetourPrevue = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DateRetourReelle = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NbProlongations = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Emprunts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Emprunts_AspNetUsers_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Emprunts_Livres_LivreId",
                        column: x => x.LivreId,
                        principalTable: "Livres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LivreId = table.Column<int>(type: "INTEGER", nullable: false),
                    UtilisateurId = table.Column<string>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreeLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotifieeLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpireLe = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_AspNetUsers_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reservations_Livres_LivreId",
                        column: x => x.LivreId,
                        principalTable: "Livres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Declencheur = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActionProposee = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ActionRetenue = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Classement = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Motif = table.Column<string>(type: "TEXT", nullable: true),
                    OutilsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourcesJson = table.Column<string>(type: "TEXT", nullable: false),
                    DonneesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Resultat = table.Column<string>(type: "TEXT", nullable: true),
                    CreeLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraiteLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DemandeurId = table.Column<string>(type: "TEXT", nullable: true),
                    EmpruntId = table.Column<int>(type: "INTEGER", nullable: true),
                    ConversationId = table.Column<int>(type: "INTEGER", nullable: true),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    ValideParId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decisions_AspNetUsers_DemandeurId",
                        column: x => x.DemandeurId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Decisions_AspNetUsers_ValideParId",
                        column: x => x.ValideParId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Decisions_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Decisions_Emprunts_EmpruntId",
                        column: x => x.EmpruntId,
                        principalTable: "Emprunts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Decisions_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Relances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpruntId = table.Column<int>(type: "INTEGER", nullable: false),
                    Niveau = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Echeance = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PrevueLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CleIdempotence = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tentatives = table.Column<int>(type: "INTEGER", nullable: false),
                    DerniereTentativeLe = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    DecisionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relances_Decisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "Decisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Relances_Emprunts_EmpruntId",
                        column: x => x.EmpruntId,
                        principalTable: "Emprunts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Relances_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CodeLiaison",
                table: "AspNetUsers",
                column: "CodeLiaison");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Telephone",
                table: "AspNetUsers",
                column: "Telephone",
                unique: true,
                filter: "Telephone IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Nom",
                table: "Categories",
                column: "Nom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Canal_Telephone",
                table: "Conversations",
                columns: new[] { "Canal", "Telephone" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Canal_UtilisateurId",
                table: "Conversations",
                columns: new[] { "Canal", "UtilisateurId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UtilisateurId",
                table: "Conversations",
                column: "UtilisateurId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_ConversationId",
                table: "Decisions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_DemandeurId",
                table: "Decisions",
                column: "DemandeurId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_EmpruntId",
                table: "Decisions",
                column: "EmpruntId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_MessageId",
                table: "Decisions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_Statut_CreeLe",
                table: "Decisions",
                columns: new[] { "Statut", "CreeLe" });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_ValideParId",
                table: "Decisions",
                column: "ValideParId");

            migrationBuilder.CreateIndex(
                name: "IX_Emprunts_DateRetourPrevue",
                table: "Emprunts",
                column: "DateRetourPrevue");

            migrationBuilder.CreateIndex(
                name: "IX_Emprunts_LivreId",
                table: "Emprunts",
                column: "LivreId");

            migrationBuilder.CreateIndex(
                name: "IX_Emprunts_UtilisateurId_DateRetourReelle",
                table: "Emprunts",
                columns: new[] { "UtilisateurId", "DateRetourReelle" });

            migrationBuilder.CreateIndex(
                name: "IX_Fragments_DocumentId_Rang",
                table: "Fragments",
                columns: new[] { "DocumentId", "Rang" });

            migrationBuilder.CreateIndex(
                name: "IX_Livres_AuteurId",
                table: "Livres",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_Livres_CategorieId",
                table: "Livres",
                column: "CategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_Livres_Isbn",
                table: "Livres",
                column: "Isbn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Livres_Titre",
                table: "Livres",
                column: "Titre");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_CleIdempotence",
                table: "Messages",
                column: "CleIdempotence",
                unique: true,
                filter: "CleIdempotence IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_IdentifiantExterne",
                table: "Messages",
                column: "IdentifiantExterne",
                unique: true,
                filter: "IdentifiantExterne IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UtilisateurId",
                table: "RefreshTokens",
                column: "UtilisateurId");

            migrationBuilder.CreateIndex(
                name: "IX_Relances_CleIdempotence",
                table: "Relances",
                column: "CleIdempotence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relances_DecisionId",
                table: "Relances",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Relances_EmpruntId_Niveau_Echeance",
                table: "Relances",
                columns: new[] { "EmpruntId", "Niveau", "Echeance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relances_MessageId",
                table: "Relances",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LivreId_Statut_CreeLe",
                table: "Reservations",
                columns: new[] { "LivreId", "Statut", "CreeLe" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_UtilisateurId",
                table: "Reservations",
                column: "UtilisateurId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Fragments");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Relances");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Decisions");

            migrationBuilder.DropTable(
                name: "Emprunts");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Livres");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Auteurs");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
