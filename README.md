# MediaLib — Bibliothèque Vivante (.NET 10)

Mini-projet .NET : port fonctionnel du projet Symfony de bibliothèque
([sheimachabchoub/symfony](https://github.com/sheimachabchoub/symfony)), entièrement réécrit en C#.
Catalogue, emprunts, réservations, ASP.NET Core Identity + JWT, agent bibliothécaire IA, RAG,
WhatsApp Business, relances planifiées et validation humaine des décisions.

## Architecture

| Projet | Rôle |
|---|---|
| `src/MediaLib.Api` | ASP.NET Core 10 Web API : EF Core (SQLite), migrations, seed, Identity, JWT + refresh token révocable, limitation de débit, OpenAPI + Scalar, services métier, agent IA, RAG, WhatsApp, relances |
| `src/MediaLib.Shared` | Uniquement DTO, contrats requête/réponse, enums et validation (ISBN, téléphone E.164). Blazor n'utilise jamais les entités EF |
| `src/MediaLib.Web` | Blazor Web App (Interactive Server) : `HttpClient` vers l'API, `AuthenticationStateProvider` personnalisé, `EditForm` + `DataAnnotationsValidator`, `AuthorizeView`, `[Authorize(Roles=…)]`, retour à la page demandée après connexion, rafraîchissement automatique du jeton |
| `tests/MediaLib.Tests` | xUnit : tests unitaires, d'intégration (`WebApplicationFactory`) et fonctionnels (97 tests) |

## Installation et lancement

Prérequis : SDK .NET 10. Aucune base à installer (SQLite, fichier `src/MediaLib.Api/medialib.db`).

```powershell
cd D:\.net\repos\MediaLib
dotnet tool restore
dotnet build
```

Créer la base (les migrations sont aussi appliquées automatiquement au démarrage, avec le seed) :

```powershell
dotnet ef database update -p src/MediaLib.Api -s src/MediaLib.Api
```

Lancer l'API (terminal 1) puis Blazor (terminal 2) :

```powershell
dotnet run --project src/MediaLib.Api --launch-profile http
```

```powershell
dotnet run --project src/MediaLib.Web --launch-profile http
```

- API + documentation Scalar : http://localhost:5032/scalar/v1 (document OpenAPI : `/openapi/v1.json`)
- Application Blazor : http://localhost:5038

Lancer les tests :

```powershell
dotnet test
```

Repartir d'une base neuve : supprimer `src/MediaLib.Api/medialib.db`, puis relancer l'API.

## Comptes de test (données de démonstration, pas de vrais secrets)

| Rôle | E-mail | Mot de passe |
|---|---|---|
| Membre | `membre@medialib.local` | `Membre123` |
| Bibliothécaire | `biblio@medialib.local` | `Biblio123` |
| Admin | `admin@medialib.local` | `Admin1234` |

Ils sont aussi définis dans `src/MediaLib.Api/Data/Seed.cs` (`ComptesDemo`). Le seed crée 10 auteurs, 5 catégories,
30 livres (ISBN-13 valides), 5 documents de connaissance publiés + 1 brouillon, et quelques emprunts
pour le membre (un en cours, un en retard, un rendu).

## Configuration et secrets

Toutes les règles sont dans `src/MediaLib.Api/appsettings.json` :

- `Bibliotheque` : `MaxEmprunts` (5), `DureeJours` (21), `JoursAvantEcheance` (2), `ProlongationJours` (7),
  `ProlongationsMax` (1), `DelaiRetraitReservationJours` (3), `TentativesMax` (3), `RelanceRetardJours` (1), `RelanceValidationJours` (7).
- `Jwt` : émetteur, audience, durée de l'access token (15 min) et du refresh token (7 j). La clé est vide dans `appsettings.json` ;
  `appsettings.Development.json` contient une clé **de développement uniquement**. En production :
  `dotnet user-secrets set "Jwt:Cle" "<clé ≥ 32 caractères>" --project src/MediaLib.Api` ou la variable `Jwt__Cle`.
- `Agent` : `Mode` = `simule` (hors ligne, par défaut) ou `claude` ; clé via
  `dotnet user-secrets set "Agent:CleApi" "<clé>" --project src/MediaLib.Api` (ou `ANTHROPIC_API_KEY`).
  Modèle par défaut : `claude-opus-5-5`. Limites : `MessagesParMinute`, `PlafondQuotidien`.
- `Messagerie:Transport` = `simule` (aucun envoi réel, tout est journalisé) ou `reel` (SMTP + WhatsApp Cloud API).
- `WhatsApp` : `Jeton`, `IdentifiantNumero`, `SecretApplication` (signature du webhook), `JetonVerification`, `NumeroPublic` — à mettre en user-secrets.
- `Planificateur` : `Actif`, `IntervalleMinutes` (traitement des relances en arrière-plan).
- `LimiteDebit` : connexions/inscriptions par minute et par IP, messages à l'agent par minute.

## Endpoints principaux (tous documentés dans Scalar)

| Méthode | Route | Accès |
|---|---|---|
| POST | `/api/auth/inscription`, `/connexion`, `/refresh` | Anonyme |
| POST | `/api/auth/deconnexion` · GET `/api/auth/moi` | Connecté |
| GET | `/api/livres` (q, categorieId, tri, page, taillePage, disponibles), `/api/livres/{id}`, `/api/auteurs`, `/api/categories` | Anonyme |
| POST/PUT/DELETE | `/api/livres`, `/api/auteurs`, `/api/categories` (409 si auteur/catégorie lié) | Bibliothécaire, Admin |
| POST | `/api/emprunts` · GET `/api/emprunts/moi`, `/api/emprunts/moi/historique` · POST `/api/emprunts/{id}/prolongation` | Membre |
| POST | `/api/emprunts/{id}/retour` · GET `/api/emprunts?enRetard=true` | Bibliothécaire, Admin |
| POST/GET/DELETE | `/api/reservations`, `/api/reservations/moi`, `/api/reservations/{id}` | Membre |
| POST | `/api/agent/messages` (anonyme possible) · GET `/api/agent/conversations` · POST `/api/agent/propositions/{id}/confirmer` | — |
| GET/POST | `/api/gestion/conversations…`, `/api/decisions…`, `/api/documents…`, `/api/relances…` | Bibliothécaire, Admin |
| GET/PUT/POST | `/api/admin/utilisateurs…` | Admin |
| GET/POST | `/api/webhooks/whatsapp` (vérification Meta, signature `X-Hub-Signature-256`) | Meta |
| PUT/POST/DELETE | `/api/compte/profil`, `/api/compte/whatsapp/…` | Connecté |

Codes : 200, 201 + `Location`, 202 (prolongation à valider), 204, 400, 401, 403, 404, 409, 429.

## Table de correspondance Symfony → .NET

| Symfony | .NET (MediaLib) |
|---|---|
| `Entity/Livre`, `Auteur`, `Category`, `Emprunt`, `Reservation` | `Api/Domain/Catalogue.cs`, `Prets.cs` (+ `NbDisponibles`, ISBN unique, jeton de concurrence) |
| `Entity/User` (Security) | `Utilisateur : IdentityUser` + rôles Identity `Membre` / `Bibliothecaire` / `Admin` |
| `security.yaml` (form login) | JWT Bearer (`AuthService`), refresh token haché et tournant (`RefreshToken`) |
| Doctrine + migrations | EF Core 10 + SQLite, `Data/Migrations` |
| Fixtures | `Data/Seed.cs` |
| `Service/EmpruntService` | `Services/EmpruntService.cs` |
| `Service/ReservationService` + `Event/LivreRendu` | `Services/ReservationService.cs` + `IObservateurRetour` |
| `Service/RelanceService` + Messenger `EnvoyerRelance` | `Services/RelanceService.cs` + `PlanificateurRelances` (`BackgroundService`) |
| `Command/DetecterRelancesCommand` | `POST /api/relances/traitement` + planificateur |
| `Service/PolitiqueActions` | `Services/Fondations.cs` (`PolitiqueActions`) |
| `Service/JournalDecisions`, `ValidationDecisions`, `DemandesAdherent` | `Services/Fondations.cs`, `Services/Decisions.cs` |
| `Communication/ServiceCommunication`, `Expediteur*` | `Communication/ServiceCommunication.cs`, `Expediteurs.cs` (e-mail SMTP, WhatsApp Graph API, simulé) |
| `Webhook/MetaWhatsappRequestParser`, `TraitementMessageEntrant` | `Communication/WebhookWhatsApp.cs` + `WebhookWhatsAppController` |
| `Service/LiaisonWhatsapp` | `Communication/LiaisonWhatsApp.cs` |
| `Connaissance/Normaliseur`, `IndexeurConnaissance`, `RechercheConnaissance` (BM25) | `Connaissance/Rag.cs` |
| `Agent/BibliothecaireIA`, `MoteurSimule`, `MoteurLlm`, `Outil/*` | `Agent/BibliothecaireIA.cs`, `MoteurSimule.cs`, `MoteurClaude.cs` (SDK officiel Anthropic), `Outils.cs` |
| `config/packages/rate_limiter.yaml` | `AddRateLimiter` (auth, agent) + `LimiteurAgent` (débit par demandeur, plafond quotidien) |
| Twig + EasyAdmin | Blazor (`MediaLib.Web`) |

## Choix techniques

- **SQLite** plutôt que LocalDB : aucune installation, même base pour les tests (SQLite en mémoire). Passer à SQL Server
  ne demande que `UseSqlServer` et une nouvelle migration.
- **Disponibilité** : `NbDisponibles` = exemplaires en rayon ; un exemplaire mis de côté pour une réservation n'est
  empruntable que par son réservataire. Jeton de concurrence optimiste sur `Livre` : le dernier exemplaire ne part qu'une fois.
- **Identité serveur pour l'agent** : aucun outil n'accepte d'identifiant d'adhérent ; l'identité vient du JWT ou d'un numéro
  WhatsApp vérifié. Un outil inconnu demandé par le modèle est refusé et journalisé (`Interdite`).
- **Moteurs IA** : simulé (règles + mêmes outils, hors ligne) ; Claude réel en mode `claude`. Repli automatique sur le moteur
  simulé si la clé manque, si l'API est en panne, si le plafond quotidien est atteint ou si le modèle refuse.
- **WhatsApp** : envoi seulement si numéro valide + lié par code + consentement ; modèles hors fenêtre de 24 h, texte libre
  dans la fenêtre ; STOP retire le consentement ; repli e-mail si WhatsApp échoue ; webhook à signature HMAC-SHA256.
- **Relances** : clé d'idempotence unique par emprunt/niveau/échéance, réessai jusqu'à `TentativesMax` puis escalade,
  annulation automatique si le livre est rendu ou l'échéance prolongée.
- **Blazor** : rendu interactif serveur sans pré-rendu ; le jeton est stocké chiffré (`ProtectedLocalStorage`) et rafraîchi
  30 s avant expiration ; en cas d'échec, retour à la connexion avec la page demandée.
- **Interface « Bibliothèque Vivante »** : palette crème/parchemin/brun/vert/terracotta/doré, Fraunces + Plus Jakarta Sans +
  Caveat, grimoire en double page avec marque-pages, composants `BookCard`, `StatusBookmark`, `RagCitationTag`, Archimède
  (hibou + lampe : repos, recherche, réponse prête), espace bibliothécaire plus sobre. Animations CSS uniquement, désactivées
  si `prefers-reduced-motion`.

## Limites connues

- La réponse de l'agent arrive en une fois (pas de streaming mot à mot) ; l'état « Archimède réfléchit » s'affiche pendant l'attente.
- Le storyboard vidéo (reel 9:16) de la charte graphique n'est pas produit ; l'accueil reprend seulement l'ouverture du livre en CSS.
- Les envois réels (SMTP, WhatsApp, Claude) n'ont pas été testés sans identifiants : les tests utilisent le transport simulé.
