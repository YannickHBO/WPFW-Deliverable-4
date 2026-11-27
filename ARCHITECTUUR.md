# Architectuurkeuzes – Portfolio API

## 1. API-stijl: controllers

Ik heb gekozen voor **controllers** met `[ApiController]`.

- **Waarom:** een portfoliosite heeft twee duidelijke resources (projecten en blogposts) met
  allemaal dezelfde CRUD-acties. Een controller per resource groepeert die vijf acties
  vanzelf. Attributen als `[Route]`, `[HttpGet]` en `[ProducesResponseType]` maken in één
  oogopslag duidelijk welke route welke statuscode teruggeeft. `[ApiController]` zorgt
  bovendien voor automatische modelvalidatie: een ongeldige DTO geeft direct een
  `400 Bad Request` met foutmeldingen, zonder dat ik daar zelf code voor schrijf. Het sluit
  ook aan op Les 5, waardoor de code herkenbaar is.
- **Het alternatief, Minimal API:** routes worden met lambda's in `Program.cs` (of in
  extensiemethodes per feature, met `MapGroup`) gekoppeld, bijvoorbeeld
  `app.MapGet("/api/projects", (IProjectService s) => s.GetAllAsync())`. Afhankelijkheden
  komen binnen via **parameterinjectie** in plaats van constructorinjectie. Minimal API heeft
  minder ceremonie en iets minder overhead, en past goed bij kleine services of een
  verticale opbouw per feature. Validatie, statuscodes en groepering moet je daarbij wel
  explicieter zelf inrichten. Voor deze omvang hadden beide gewerkt. Ik koos controllers
  vanwege de vaste structuur en de ingebouwde validatie.

## 2. Code-organisatie: lagen

| Laag | Map | Verantwoordelijkheid |
|---|---|---|
| HTTP | `Controllers/` | Route, request-body ontvangen, wie wat mag (`[Authorize]`), juiste statuscode teruggeven (`200/201/204/400/401/403/404`). Geen databasecode. |
| Applicatielogica | `Services/` + `Mappings/` | Use cases (ophalen, aanmaken, wijzigen, verwijderen), sortering, omzetten tussen entiteit en DTO. |
| Data | `Data/` + `Models/` + `Migrations/` | `PortfolioDbContext`, entiteiten, tabelconfiguratie (maxlengtes, seed-data) en schema-migraties. |
| Contract | `Dtos/` | Wat de buitenwereld ziet en mag sturen. |

**Waarom lagen en geen verticale features:** de app heeft maar twee features, die ook nog
precies hetzelfde patroon volgen. In een gelaagde structuur vind je per soort
verantwoordelijkheid alles op één plek. Een verticale opbouw (bijvoorbeeld
`Features/Projects/CreateProject.cs` met endpoint, DTO en handler bij elkaar) wordt pas
voordelig als er veel features komen die verschillende logica hebben.

De controller kent alleen de **interface** van de service (`IProjectService`). De service
gebruikt de `DbContext`. Daardoor kan de logica los van HTTP getest of vervangen worden.

## 3. DTO's in plaats van entiteiten

Geen enkel endpoint ontvangt of verstuurt een EF Core-entiteit. Per resource zijn er drie DTO's:

- `ProjectDto` – **uitgaand**, bevat het `Id`.
- `CreateProjectDto` – **inkomend bij POST**, zonder `Id`, want de database genereert dat.
  Zo kan een client geen id forceren (*overposting*).
- `UpdateProjectDto` – **inkomend bij PUT**, het `Id` komt uit de route.

Voordelen: het databaseschema kan veranderen zonder dat het API-contract breekt, de
validatieregels (`[Required]`, `[StringLength]`, `[Url]`) staan op het contract waar ze
horen, en er lekken nooit velden naar buiten die niet bedoeld zijn om te tonen. De
mapping staat in eigen extensiemethodes (`ToDto`, `ToEntity`, `ApplyTo`) in `Mappings/`,
zodat je die makkelijk terugvindt.

## 4. Dependency injection

In `Program.cs` worden geregistreerd:

```csharp
builder.Services.AddDbContext<PortfolioDbContext>(o => o.UseSqlServer(...)); // scoped
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IBlogPostService, BlogPostService>();
```

Controllers krijgen de service via **constructorinjectie**, en services krijgen de
`DbContext` ook via de constructor. Nergens wordt met `new` een `DbContext` gemaakt. De
container maakt er één per request aan en ruimt die daarna op. **Scoped** is de juiste
levensduur, omdat een `DbContext` niet thread-safe is en per unit-of-work hoort te leven.

## 5. Opslag: EF Core + SQL Server met migraties

SQL Server zorgt ervoor dat gegevens een herstart overleven, wat met de InMemory-provider
niet zou lukken. Het schema en de seed data worden opgebouwd met **migraties**, te beginnen bij
`InitialCreate`, die bij het opstarten met `Database.Migrate()` worden toegepast. Anders dan bij `EnsureCreated` kan het
schema later stap voor stap meegroeien met de site (bijvoorbeeld tags of een
`ImageUrl` op een project) zonder dat bestaande projecten en blogposts verloren gaan.

## 6. Waarom dit past bij een portfoliosite

Een portfoliosite is klein, maar groeit wel: er komen projecten en blogposts bij, en
misschien later een beheerpagina of authenticatie. De gekozen opzet houdt de API
voorspelbaar (REST-routes, standaard statuscodes, gedocumenteerd in Swagger). Uitbreiden
blijft ook eenvoudig: een nieuw veld kost een DTO-veld, een mappingregel en een migratie,
zonder dat de controllers veranderen.

## 7. Authenticatie en autorisatie

**Authenticatie** is vaststellen *wie* iemand is (inloggen). **Autorisatie** is bepalen *wat* die
persoon mag. Iedereen mag projecten en blogposts lezen; alleen een Editor of Admin mag ze aanmaken,
wijzigen en verwijderen.

### 7.1 ASP.NET Core Identity in dezelfde database

Ik gebruik **ASP.NET Core Identity met EF Core** en schrijf geen eigen wachtwoordopslag: het hashen
van wachtwoorden (PBKDF2 met salt), lockout en het beheren van rollen zijn standaardfuncties die
door Microsoft onderhouden en getest worden. `PortfolioDbContext` erft van `IdentityDbContext<User>`,
zodat de accounttabellen (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, ...) in dezelfde database
en met dezelfde migraties meekomen als de portfolio-inhoud. Eén database betekent één connectionstring
en één migratiegeschiedenis.

`User` erft van `IdentityUser` en heeft nog geen eigen velden. De klasse bestaat zodat ik later velden
kan toevoegen zonder overal `IdentityUser` te moeten vervangen.

### 7.2 Een cookie in plaats van een token

Na een geslaagde login zet de API een **authenticatiecookie**. De browser bewaart die en stuurt hem
automatisch mee; de React-app hoeft dus geen token op te slaan of zelf in een header te zetten.
Dat is ook veiliger: een token in `localStorage` kan door kwaadaardige JavaScript (XSS) worden
gelezen, een `HttpOnly`-cookie niet. Swagger UI werkt met een cookie vanzelf, zonder extra configuratie.

De cookie-instellingen:

| Instelling | Waarde | Waarom |
|---|---|---|
| `HttpOnly` | aan | JavaScript kan de cookie niet lezen (bescherming tegen XSS). |
| `Secure` | altijd | Alleen via HTTPS. Browsers accepteren hem ook op `http://localhost` voor ontwikkeling. |
| `SameSite` | `Lax` | De cookie gaat niet mee met een POST/PUT/DELETE vanaf een andere site (bescherming tegen CSRF). |
| Levensduur | 8 uur, verlengd bij gebruik | Een vergeten sessie verloopt vanzelf. |

**Waarom geen apart antiforgery-token:** door `SameSite=Lax` stuurt de browser de cookie niet mee bij
een schrijfverzoek dat van een andere site komt. Een JSON-verzoek vanaf een ander domein vraagt
bovendien eerst toestemming (CORS-preflight), en die staat niet open.

**Voorbereid op de SPA:** in ontwikkeling stuurt Vite `/api` door naar de API, en bij hosting doet een
proxyregel (bijvoorbeeld op Netlify) hetzelfde. De browser praat dan maar met één domein, zodat de
cookie een *first-party* cookie blijft en niet door browsers als third-party cookie wordt geblokkeerd.

### 7.3 Een eigen `AuthController` en `AddIdentityCore`

Identity heeft kant-en-klare routes (`MapIdentityApi`), maar daarin zit ook een eigen `/register` die
geen rol toekent. Die route kan niet los worden uitgezet, dus zou er een registratie zonder de rol
Gast mogelijk blijven. Daarom heb ik de vier routes zelf geschreven in `AuthController`:

- **`register`** maakt met `UserManager.CreateAsync` het account aan (wachtwoordregels en uniek
  e-mailadres worden door Identity gecontroleerd) en kent met `AddToRoleAsync` **alleen de rol Gast**
  toe. Mislukt dat laatste, dan wordt het account weer verwijderd: er bestaat nooit een account zonder rol.
  Registreren logt niet automatisch in; registratie en login zijn twee aparte stappen.
- **`login`** gebruikt `SignInManager.PasswordSignInAsync`, dat het wachtwoord controleert en de cookie zet.
- **`logout`** verwijdert de cookie.
- **`me`** laat zien wie er is aangemeld en met welke rollen. De React-app gebruikt dat later om te
  bepalen welke knoppen getoond worden.

In `Program.cs` staat `AddIdentityCore` in plaats van `AddIdentity` of `AddIdentityApiEndpoints`.
`AddIdentity` is bedoeld voor websites met loginpagina's, en `AddIdentityApiEndpoints` voegt ook
bearer-tokens toe die deze API niet gebruikt. `AddIdentityCore` voegt alleen toe wat nodig is; de
cookie wordt apart ingesteld met `AddIdentityCookies`.

Omdat dit een API is, geeft een niet-toegestaan verzoek een **statuscode** in plaats van een redirect
naar een loginpagina: **401** zonder geldige aanmelding, **403** als de aanmelding wel geldig is maar de
rol ontbreekt.

### 7.4 Rollen en één policy

Er zijn drie rollen: **Gast**, **Editor** en **Admin**. Ze worden met `HasData` in een migratie
aangemaakt, met vaste Ids, zodat ze in elke database bestaan zodra de migraties zijn uitgevoerd.

De schrijfregel staat op één plek, als policy:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanEditContent, policy => policy.RequireRole(RoleNames.Editor, RoleNames.Admin));
```

**Admin staat er expliciet in.** Rollen in ASP.NET Core kennen geen rangorde: Admin heeft niet
vanzelf de rechten van Editor. Door beide rollen in de policy te noemen, is in de code te lezen wie
mag schrijven.

De policy staat met `[Authorize(Policy = Policies.CanEditContent)]` **op elke schrijfactie**, zoals in
de les. Het alternatief is de policy op de hele controller zetten en de GET-routes vrijgeven met
`[AllowAnonymous]`; dan is een nieuwe route standaard afgeschermd. Ik heb gekozen voor de variant per
actie, omdat je bij elke route direct ziet wat er geldt. De integratietesten controleren dat geen
enkele schrijfroute zonder deze policy bereikbaar is.

De rol- en policynamen staan als constanten in `Auth/RoleNames.cs` en `Auth/Policies.cs`, zodat een
typfout een compileerfout geeft in plaats van een rol die stilletjes niet werkt.

### 7.5 Opnieuw inloggen na een rolwijziging

Iedere gebruiker heeft een **SecurityStamp**: een willekeurige waarde die ook in de cookie staat. De
API vergelijkt bij **elk verzoek** de stamp in de cookie met die in de database
(`SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`). Verandert de stamp, dan is de
cookie ongeldig (**401**) en moet de gebruiker opnieuw inloggen.

Tijdens het testen bleek dat **een rolwijziging alleen niet genoeg is**: de API haalt bij die controle
de rollen opnieuw uit de database, dus een nieuwe rol werkte direct, zonder opnieuw in te loggen. Om
opnieuw inloggen af te dwingen, verandert daarom **bij elke rolwijziging ook de SecurityStamp**: het
SQL-script doet dat in dezelfde transactie als het toekennen van de rol.

De prijs is één extra databasequery per aangemeld verzoek. Voor een portfoliosite is dat verwaarloosbaar,
en het voordeel is dat een ingetrokken recht direct ingaat.

### 7.6 Editor en Admin toekennen: een lokaal SQL-script

Registratie kent nooit Editor of Admin toe. In de les werd een admin-account met een wachtwoord in de
code geseed; dat zou een wachtwoord in de repository zetten, en dat mag niet. Daarom maakt de seed alleen
de **rollen** aan, en ken ik Editor en Admin toe met `scripts/assign-role.sql`. De gebruiker registreert
eerst zelf (met een eigen wachtwoord dat alleen als hash in de lokale database staat), daarna draai ik
het script met het e-mailadres en de rol als `sqlcmd`-variabelen. Er staan dus geen e-mailadressen of
wachtwoorden in de repository.

### 7.7 Overige beveiligingskeuzes

- **Lockout na 3 foute wachtwoorden** (5 minuten). Dat maakt wachtwoorden raden (brute force) veel
  trager. De keerzijde is dat iemand een account tijdelijk kan blokkeren door expres foute wachtwoorden
  te proberen; door de korte duur blijft die schade beperkt.
- **Eén foutmelding bij inloggen** voor een onbekend e-mailadres, een fout wachtwoord en een geblokkeerd
  account, zodat de login niet verraadt welke e-mailadressen een account hebben.
- **Registratie meldt wél dat een e-mailadres al bestaat.** Dat is een bewuste afweging: met een
  algemene melding kan een gebruiker niet zien waarom de registratie mislukt.
- **Geen geheimen in de repository:** de connectionstring gebruikt Windows-authenticatie, een SQL-login
  hoort in user-secrets, en de sleutels waarmee de cookie versleuteld wordt (Data Protection) staan in
  het Windows-profiel.
