# Testen – Portfolio API

Het xUnit-testproject [`PortfolioApi.Tests`](PortfolioApi.Tests) staat in dezelfde solution als de API
(`PortfolioApi.slnx`). Alle tests volgen **Arrange, Act, Assert** (met die drie commentaarregels in elke
test) en de naamgeving `Methode_Scenario_VerwachtResultaat`.

## Tests uitvoeren

```bash
dotnet test
```

Uitkomst: **146 tests, allemaal geslaagd**, in ongeveer 2 seconden. Er is geen SQL Server, internet of
GitHub-account voor nodig.

## Opbouw

| Map | Wat | Hoe |
|---|---|---|
| `Integration/` | Alle routes **via HTTP** | `WebApplicationFactory` start de echte API (dezelfde `Program.cs`, controllers, Identity, cookie en policies) in het geheugen. Een `HttpClient` gedraagt zich als een browser: hij bewaart de cookie en praat HTTPS. |
| `Unit/` | Services, validatieregels, mappings, GitHub-client en achtergrondservice **los** | Direct aangeroepen, met een eigen database per test en zelfgemaakte fakes. |
| `Support/` | Hulpcode | `PortfolioApiFactory`, `FakeGitHubCommitClient`, `FixedTimeProvider`, `SqliteTestDatabase`. |

**Testdata is geïsoleerd van de gewone database.** De tests gebruiken nooit `PortfolioDb`: elke
integratietestklasse en elke unit test krijgt een eigen **SQLite-database in het geheugen**, aangemaakt uit
het model met dezelfde seed data (projecten, blogposts, rollen). Na de test bestaat die database niet meer.
Ook de buitenwereld is vervangen: GitHub door een fake (de tests roepen GitHub nooit aan) en de
sleutels waarmee de cookie versleuteld wordt door tijdelijke sleutels.

**Zelfgemaakte fakes, zoals in de les:** `FakeGitHubCommitClient` (commits, ETag, onbereikbaar),
`FixedTimeProvider` (vaste klok), en kleine subklassen in de tests zelf: een `DbContext` die niet kan
opslaan, een `UserManager` waarvan het vernieuwen van de SecurityStamp mislukt, een `HttpMessageHandler`
die een vast GitHub-antwoord geeft, en een sync-service die telt of een fout gooit.

## Route- en testmatrix

Testnamen staan als `Klasse.Methode`; alle klassen staan in `PortfolioApi.Tests/Integration/`.
`[Theory]`-tests draaien voor elke genoemde rol apart.

### Projecten

| Route | Toegang | Scenario → verwacht resultaat | Test |
|---|---|---|---|
| `GET /api/projects` | Iedereen | Anoniem → **200**, seed-projecten, lopende eerst en dan nieuwste eerst | `ProjectsRoutesTests.GetAll_Anonymous_Returns200WithSeededProjectsInProgressFirstThenNewest` |
| `GET /api/projects/{id}` | Iedereen | Bestaand id → **200** met de juiste gegevens | `ProjectsRoutesTests.GetById_ExistingId_Returns200WithThatProject` |
| | | Onbekend id → **404** | `ProjectsRoutesTests.GetById_UnknownId_Returns404` |
| `POST /api/projects` | Editor, Admin | Anoniem → **401**, niets opgeslagen | `ProjectsRoutesTests.Create_Anonymous_Returns401AndStoresNothing` |
| | | Gast → **403**, niets opgeslagen | `ProjectsRoutesTests.Create_AsGuest_Returns403AndStoresNothing` |
| | | Editor / Admin → **201** + `Location`, project staat in de database | `ProjectsRoutesTests.Create_AsEditorOrAdmin_Returns201AndStoresTheProject` (Editor, Admin) |
| | | Ongeldige invoer (lege titel) → **400**, niets opgeslagen | `ProjectsRoutesTests.Create_AsEditorWithInvalidBody_Returns400AndStoresNothing` |
| | | Vier tags → **400** | `ProjectsRoutesTests.Create_AsEditorWithFourTags_Returns400` |
| `PUT /api/projects/{id}` | Editor, Admin | Anoniem → **401**, niets gewijzigd | `ProjectsRoutesTests.Update_Anonymous_Returns401AndChangesNothing` |
| | | Gast → **403**, niets gewijzigd | `ProjectsRoutesTests.Update_AsGuest_Returns403AndChangesNothing` |
| | | Editor / Admin → **200**, wijziging staat in de database (tags vervangen) | `ProjectsRoutesTests.Update_AsEditorOrAdmin_Returns200AndStoresTheChanges` (Editor, Admin) |
| | | Onbekend id → **404** | `ProjectsRoutesTests.Update_AsEditorUnknownId_Returns404` |
| | | Ongeldige invoer (dubbele tags) → **400**, niets gewijzigd | `ProjectsRoutesTests.Update_AsEditorWithInvalidBody_Returns400AndChangesNothing` |
| `DELETE /api/projects/{id}` | Editor, Admin | Anoniem → **401**, project bestaat nog | `ProjectsRoutesTests.Delete_Anonymous_Returns401AndKeepsTheProject` |
| | | Gast → **403**, project bestaat nog | `ProjectsRoutesTests.Delete_AsGuest_Returns403AndKeepsTheProject` |
| | | Editor / Admin → **204**, project weg (database én `GET` → 404) | `ProjectsRoutesTests.Delete_AsEditorOrAdmin_Returns204AndRemovesTheProject` (Editor, Admin) |
| | | Onbekend id → **404** | `ProjectsRoutesTests.Delete_AsEditorUnknownId_Returns404` |

### Blogposts

| Route | Toegang | Scenario → verwacht resultaat | Test |
|---|---|---|---|
| `GET /api/blogposts` | Iedereen | Anoniem → **200**, seed-posts, nieuwste eerst | `BlogPostsRoutesTests.GetAll_Anonymous_Returns200WithSeededPostsNewestFirst` |
| `GET /api/blogposts/{id}` | Iedereen | Handgeschreven post → **200**, foto's in volgorde, geen commit | `BlogPostsRoutesTests.GetById_HandWrittenPostWithGallery_Returns200WithImagesInOrderAndNoCommit` |
| | | Commitpost → **200** met `commitSha` en GitHub-link | `BlogPostsRoutesTests.GetById_CommitPost_Returns200WithCommitShaAndGitHubLink` |
| | | Onbekend id → **404** | `BlogPostsRoutesTests.GetById_UnknownId_Returns404` |
| `POST /api/blogposts` | Editor, Admin | Anoniem → **401**, niets opgeslagen | `BlogPostsRoutesTests.Create_Anonymous_Returns401AndStoresNothing` |
| | | Gast → **403**, niets opgeslagen | `BlogPostsRoutesTests.Create_AsGuest_Returns403AndStoresNothing` |
| | | Editor / Admin → **201** + `Location`, post en foto's staan in de database | `BlogPostsRoutesTests.Create_AsEditorOrAdmin_Returns201AndStoresThePostWithImages` (Editor, Admin) |
| | | `commitSha` meesturen → genegeerd (geen nep-commit) | `BlogPostsRoutesTests.Create_AsEditorWithCommitShaInBody_IgnoresIt` |
| | | Ongeldige invoer (lege tekst) → **400**, niets opgeslagen | `BlogPostsRoutesTests.Create_AsEditorWithInvalidBody_Returns400AndStoresNothing` |
| | | Elf foto's → **400** | `BlogPostsRoutesTests.Create_AsEditorWithElevenImages_Returns400` |
| `PUT /api/blogposts/{id}` | Editor, Admin | Anoniem → **401**, niets gewijzigd | `BlogPostsRoutesTests.Update_Anonymous_Returns401AndChangesNothing` |
| | | Gast → **403**, niets gewijzigd | `BlogPostsRoutesTests.Update_AsGuest_Returns403AndChangesNothing` |
| | | Editor / Admin → **200**, tekst gewijzigd en foto's vervangen | `BlogPostsRoutesTests.Update_AsEditorOrAdmin_Returns200AndReplacesTextAndImages` (Editor, Admin) |
| | | Commitpost wijzigen → SHA blijft staan | `BlogPostsRoutesTests.Update_AsEditorOnCommitPost_KeepsTheCommitSha` |
| | | Onbekend id → **404** | `BlogPostsRoutesTests.Update_AsEditorUnknownId_Returns404` |
| | | Ongeldige invoer (titel te lang) → **400**, niets gewijzigd | `BlogPostsRoutesTests.Update_AsEditorWithInvalidBody_Returns400AndChangesNothing` |
| `DELETE /api/blogposts/{id}` | Editor, Admin | Anoniem → **401**, post bestaat nog | `BlogPostsRoutesTests.Delete_Anonymous_Returns401AndKeepsThePost` |
| | | Gast → **403**, post bestaat nog | `BlogPostsRoutesTests.Delete_AsGuest_Returns403AndKeepsThePost` |
| | | Editor / Admin → **204**, post én foto's weg | `BlogPostsRoutesTests.Delete_AsEditorOrAdmin_Returns204AndRemovesThePostAndItsImages` (Editor, Admin) |
| | | Onbekend id → **404** | `BlogPostsRoutesTests.Delete_AsEditorUnknownId_Returns404` |

### Registratie en aanmelding

| Route | Toegang | Scenario → verwacht resultaat | Test |
|---|---|---|---|
| `POST /api/auth/register` | Iedereen | Geldig → **201**, alleen rol Gast (ook in de database) | `AuthRoutesTests.Register_ValidEmailAndPassword_Returns201AndGivesOnlyTheRoleGast` |
| | | Rollen in de body → toch alleen Gast | `AuthRoutesTests.Register_WithRolesInTheBody_StillOnlyGivesGast` |
| | | Logt niet automatisch in (`me` → 401) | `AuthRoutesTests.Register_DoesNotSignIn` |
| | | Bestaand e-mailadres → **400** | `AuthRoutesTests.Register_ExistingEmail_Returns400` |
| | | Zwak wachtwoord (4 regels) → **400**, geen account | `AuthRoutesTests.Register_WeakPassword_Returns400WithIdentityRule` |
| | | Ongeldig e-mailadres → **400** | `AuthRoutesTests.Register_InvalidEmail_Returns400` |
| `POST /api/auth/login` | Iedereen | Juist wachtwoord → **200** + cookie `HttpOnly`, `Secure`, `SameSite=Lax` | `AuthRoutesTests.Login_CorrectPassword_Returns200AndASecureHttpOnlyCookie` |
| | | Fout wachtwoord → **401**, geen cookie | `AuthRoutesTests.Login_WrongPassword_Returns401WithoutCookie` |
| | | Onbekend e-mailadres → **401**, zelfde melding | `AuthRoutesTests.Login_UnknownEmail_Returns401WithSameMessageAsWrongPassword` |
| | | Na 3 foute pogingen → **401**, ook met het juiste wachtwoord | `AuthRoutesTests.Login_AfterThreeWrongPasswords_IsLockedOutEvenWithTheRightPassword` |
| | | 2 fout, dan goed → **200**, teller terug op 0 | `AuthRoutesTests.Login_TwoWrongPasswordsThenTheRightOne_SignsInAndResetsTheCounter` |
| | | Lege velden → **400** | `AuthRoutesTests.Login_InvalidBody_Returns400` |
| | | Na rolwijziging opnieuw inloggen → cookie met de nieuwe rol | `AuthRoutesTests.Login_AfterRoleChange_GivesACookieWithTheNewRole` |
| `GET /api/auth/me` | Aangemeld | Gast / Editor / Admin → **200** met de rollen | `AuthRoutesTests.Me_SignedIn_Returns200WithTheRolesFromTheCookie` (Gast, Editor, Admin) |
| | | Anoniem → **401**, geen redirect | `AuthRoutesTests.Me_Anonymous_Returns401NotARedirect` |
| | | Na wijziging SecurityStamp → **401** | `AuthRoutesTests.Me_AfterSecurityStampChange_Returns401SoTheUserMustSignInAgain` |
| `POST /api/auth/logout` | Iedereen | Aangemeld → **204**, cookie werkt niet meer | `AuthRoutesTests.Logout_SignedIn_Returns204AndTheCookieNoLongerWorks` |
| | | Anoniem → **204** | `AuthRoutesTests.Logout_Anonymous_Returns204` |

### Beheerroutes (uitbreiding)

| Route | Toegang | Scenario → verwacht resultaat | Test |
|---|---|---|---|
| `GET /api/admin/users` | Admin | Anoniem / Gast / Editor → **401 / 403 / 403** | `AdminUsersRoutesTests.GetAll_NotAdmin_IsRefused` |
| | | Admin → **200** met gebruikers en rollen | `AdminUsersRoutesTests.GetAll_AsAdmin_Returns200WithUsersAndTheirRoles` |
| `POST /api/admin/users/{id}/editor` | Admin | Anoniem / Gast / Editor → **401 / 403 / 403**, niets gewijzigd | `AdminUsersRoutesTests.GrantEditor_NotAdmin_IsRefusedAndChangesNothing` |
| | | Admin, voor een Gast → **200**; oude cookie → 401; na opnieuw inloggen mag de gebruiker schrijven | `AdminUsersRoutesTests.GrantEditor_AsAdminForAGuest_Returns200AndTheGuestIsEditorAfterSigningInAgain` |
| | | De Admin blijft aangemeld | `AdminUsersRoutesTests.GrantEditor_AsAdmin_DoesNotSignTheAdminOut` |
| | | Onbekende gebruiker → **404** | `AdminUsersRoutesTests.GrantEditor_UnknownUser_Returns404` |
| | | Al Editor of Admin → **409**, niets gewijzigd | `AdminUsersRoutesTests.GrantEditor_UserIsAlreadyEditorOrAdmin_Returns409AndChangesNothing` |
| `POST /api/admin/sync-commits` | Admin | Anoniem / Gast / Editor → **401 / 403 / 403**, GitHub niet aangeroepen | `AdminCommitsRoutesTests.SyncCommits_NotAdmin_IsRefusedAndGitHubIsNotCalled` |
| | | Admin → **200**, nieuwe commit wordt post, merge overgeslagen | `AdminCommitsRoutesTests.SyncCommits_AsAdmin_AddsTheNewCommitAsBlogPostAndSkipsTheMerge` |
| | | Tweede keer zonder wijzigingen → `NotModified` (ETag) | `AdminCommitsRoutesTests.SyncCommits_TwiceWithoutNewCommits_SecondTimeReturnsNotModified` |
| | | GitHub onbereikbaar → **502**, blog ongewijzigd | `AdminCommitsRoutesTests.SyncCommits_GitHubUnreachable_Returns502AndKeepsTheBlogAsItWas` |

### Vangnet voor de autorisatieregels

`AuthorizationRulesTests` controleert de regels zelf, niet alleen de routes van nu:

- `EveryWriteRouteOfEveryController_RequiresAPolicy_ExceptRegisterLoginLogout`: zodra iemand een
  POST/PUT/DELETE-route toevoegt zonder policy, faalt deze test.
- `ContentControllers_WritesNeedCanEditContent_ReadsArePublic`: POST, PUT en DELETE van projecten en
  blogposts hebben `CanEditContent`; de GET-routes geen policy.
- `Policy_AllowsExactlyTheseRoles`: `CanEditContent` = precies Editor en Admin, `IsAdmin` = precies Admin.

**Gecontroleerd dat de tests fouten vinden:** als proef heb ik tijdelijk `[Authorize]` van
`ProjectsController.Create` gehaald en registratie de rol Editor laten geven. Daarop faalden 6 tests,
precies de tests die dit horen te zien. Daarna is de code teruggezet.

## Unit tests

| Klasse | Wat |
|---|---|
| `ProjectServiceTests`, `BlogPostServiceTests` | De logica achter de CRUD-routes zonder HTTP: sortering, aanmaken, wijzigen (foto's vervangen), verwijderen (foto's mee), onbekend id. |
| `UserAdminServiceTests` | Gast → Editor vernieuwt de SecurityStamp; **mislukt dat, dan wordt de rol teruggedraaid** (transactie); alleen een gewone Gast kan Editor worden. |
| `CommitSyncServiceTests` | Commit → post: nummering, tekst, Nederlandse tijd (winter- en zomertijd), merge overslaan, bestaande post herkennen, verwijderd blijft verwijderd, ETag, GitHub onbereikbaar of time-out, ETag niet onthouden als opslaan mislukt. |
| `GitHubCommitClientTests` | Welke URL, het lezen van GitHubs JSON, `If-None-Match` en 304, token als Bearer, foutstatussen. |
| `CommitSyncBackgroundServiceTests` | Synchroniseert bij het starten, doet niets als het uit staat, blijft draaien na een fout. |
| `ProjectTagRulesTests`, `BlogPostMappingsTests` | Tagregels (max. 3, 1–30 tekens, uniek) en de commitvelden in de mapping. |

## Coverage

```bash
dotnet test --settings PortfolioApi.Tests/coverage.runsettings --results-directory TestResults
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:"Html;TextSummary"
```

Open daarna `coveragereport/index.html`. ReportGenerator installeer je eenmalig met
`dotnet tool install -g dotnet-reportgenerator-globaltool`. `TestResults/` en `coveragereport/` staan in
`.gitignore`.

[`coverage.runsettings`](PortfolioApi.Tests/coverage.runsettings) meet **alleen de code van de API**: niet de
tests en niet de EF Core-migraties. Die migraties zijn door `dotnet ef` gegenereerd en bevatten geen eigen
logica; de tests bouwen hun SQLite-database uit het model in plaats van ze uit te voeren.

| | Resultaat |
|---|---|
| **Line coverage** | **97,3 %** (706 van 725 regels) |
| **Branch coverage** | **92,8 %** (91 van 98 takken) |
| **Method coverage** | **99,3 %** (155 van 156 methodes) |
| Controllers, services, mappings, DTO's | 100 %, behalve `AuthController` (90,9 %) |

### Onderbouwing

De opdracht vraagt geen percentage, maar dat **alle functionaliteit van de CRUD-routes** getest is en dat
ik kan uitleggen wat de coverage betekent. Daarom eerst wat de tests aantonen, daarna wat ze niet raken.

**Wat de tests aantonen.** Alle tien CRUD-routes en de registratie- en loginroute zijn via HTTP getest met de
echte Identity-configuratie. Elke schrijfroute is getest zonder aanmelding (401), als Gast (403) en als
Editor én Admin (geslaagd). Na elke POST, PUT en DELETE controleert de test in de database wat er met de
gegevens is gebeurd, en bij een geweigerd verzoek dat er níets is gebeurd. Ongeldige invoer (400) en een
onbekend id (404) zijn voor elke route met invoer of id getest. Line coverage zegt alleen dat een regel is
uitgevoerd; dat de uitkomst klopt, komt uit die asserts.

**Wat niet gedekt is, en waarom** (de overgebleven 19 regels en 7 takken):

| Code | Waarom niet getest |
|---|---|
| `Program.cs`: `Database.Migrate()` bij het starten | Staat in de tests bewust uit: de migraties zijn voor SQL Server, de tests gebruiken SQLite. Handmatig getest door de API op een lege SQL Server-database te starten. |
| `Program.cs`: Swagger | Alleen in `Development`; de tests draaien in de omgeving `Testing`. |
| `Program.cs`: instellingen van de echte GitHub-`HttpClient` | In de tests vervangen door de fake. Het gedrag van de client zelf is getest in `GitHubCommitClientTests`; dat GitHub deze headers accepteert, is handmatig tegen de echte GitHub API getest. |
| `AuthController.Register`: account verwijderen als de rol Gast niet kan worden toegekend | Kan alleen als de rol Gast niet in de database staat; die wordt door de migraties altijd aangemaakt. Het is een vangnet, geen normaal pad. |
| `AuthController.Me`: terugvallen op de naam als de cookie geen e-mail bevat | Identity zet altijd een e-mail in de cookie; ook dit is een vangnet. |
| `BlogPostImage.BlogPost` | Navigatie-eigenschap voor EF Core, die door de code niet wordt gelezen. |
| `CommitSyncBackgroundService`: de tweede ronde van de timer | Komt pas na 15 minuten; de eerste ronde, uitzetten en foutafhandeling zijn wel getest. |
| `GitHubCommitClient`: GitHub antwoordt met `null` | Doet GitHub niet; de code vangt het af zodat het geen fout geeft. |
| `ProjectTagRules`: een tag die `null` is | JSON-invoer van een lijst met `null` erin; de regel geeft dan een validatiefout, maar dat specifieke geval is niet apart getest. |

**Wat deze tests niet kunnen aantonen.** SQLite is geen SQL Server. Gedrag dat specifiek is voor SQL Server,
zoals het filter op de unieke index van `CommitSha`, de migraties zelf en `QUOTED_IDENTIFIER` in het
SQL-script, is met de hand getest op een aparte SQL Server-database (zie de rooktests in de pull requests).
Ook de React-frontend valt buiten deze tests.
