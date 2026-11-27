# WPFWDesigns Portfolio

React-frontend met een ASP.NET Core Web API (.NET 10) die projecten en blogposts in SQL Server opslaat.
Accounts en rollen via ASP.NET Core Identity, tests met xUnit.

- Ontwerpkeuzes: [ARCHITECTUUR.md](ARCHITECTUUR.md)
- Tests en coverage: [TESTEN.md](TESTEN.md)
- Hosting op Azure en Netlify: [DEPLOYMENT.md](DEPLOYMENT.md)

## Projectstructuur

```
PortfolioApi/          Web API (controllers → services → EF Core)
PortfolioApi.Tests/    xUnit: Integration/ (via HTTP), Unit/, Support/
client/                React-frontend (Vite)
scripts/               assign-role.sql: rol toekennen
PortfolioApi.slnx      Solution met API en testproject
```

## Lokaal starten

Nodig: .NET 10 SDK, SQL Server Express (`.\SQLEXPRESS`) en Node.js.

De connectionstring staat in `PortfolioApi/appsettings.json` en gebruikt Windows-authenticatie. Gebruik je
LocalDB, vervang dan de server door `(localdb)\\MSSQLLocalDB`. Een SQL-login met wachtwoord zet je in de
user-secrets, niet in `appsettings.json`:

```bash
cd PortfolioApi
dotnet user-secrets set "ConnectionStrings:PortfolioDb" "Server=...;Database=PortfolioDb;User Id=...;Password=..."
```

API starten:

```bash
cd PortfolioApi
dotnet run --launch-profile http
```

Bij het starten voert de API de EF Core-migraties uit. Daarmee worden de database `PortfolioDb`, de
Identity-tabellen, de rollen Gast, Editor en Admin en de seed data aangemaakt. Handmatig kan ook:

```bash
dotnet tool install --global dotnet-ef --version 10.0.14
cd PortfolioApi
dotnet ef database update
```

Swagger UI: http://localhost:5080/swagger

Frontend starten (met de API aan; Vite stuurt `/api` door naar poort 5080):

```bash
cd client
npm install
npm run dev
```

## Gebruikers en rollen

| Rol | Hoe | Mag |
|---|---|---|
| Gast | Elke nieuwe registratie | Lezen |
| Editor | Script of Admin | Projecten en blogposts aanmaken, wijzigen, verwijderen |
| Admin | Script | Hetzelfde als Editor, plus gebruikersbeheer |

**Gewone gebruiker:** in Swagger `POST /api/auth/register` met een e-mailadres en wachtwoord (minimaal 6 tekens,
hoofdletter, kleine letter, cijfer en speciaal teken). Daarna `POST /api/auth/login`; de API zet een
authenticatiecookie.

**Editor of Admin:** registreer eerst, voer daarna vanuit de root uit:

```bash
sqlcmd -S .\SQLEXPRESS -E -C -b -W -d PortfolioDb -i scripts/assign-role.sql -v Email="editor@example.test" Role="Editor"
sqlcmd -S .\SQLEXPRESS -E -C -b -W -d PortfolioDb -i scripts/assign-role.sql -v Email="admin@example.test" Role="Admin"
```

Een Admin kan een Gast ook Editor maken met `POST /api/admin/users/{id}/editor`.

**Na een rolwijziging moet de gebruiker opnieuw inloggen.** Het script en de admin-route vernieuwen de
SecurityStamp, en de API controleert die bij elk verzoek. De oude cookie geeft dan 401.

Na 3 foute wachtwoorden is een account 5 minuten geblokkeerd. Er staan geen wachtwoorden of sleutels in de
repository.

## Routes

| Route | Toegang |
|---|---|
| `GET /api/projects`, `GET /api/projects/{id}` | Iedereen |
| `POST`, `PUT`, `DELETE /api/projects` | Editor, Admin |
| `GET /api/blogposts`, `GET /api/blogposts/{id}` | Iedereen |
| `POST`, `PUT`, `DELETE /api/blogposts` | Editor, Admin |
| `POST /api/auth/register`, `/login`, `/logout` | Iedereen |
| `GET /api/auth/me` | Aangemeld |
| `GET /api/admin/users`, `POST /api/admin/users/{id}/editor` | Admin |
| `POST /api/admin/sync-commits` | Admin |

Een schrijfverzoek zonder aanmelding geeft 401, een aangemelde gebruiker zonder Editor- of Admin-rol 403.
De statuscodes per route en de bijbehorende tests staan in [TESTEN.md](TESTEN.md).

## Testen

```bash
dotnet test
```

De tests draaien op een eigen SQLite-database in het geheugen en gebruiken een nep-GitHub; SQL Server en
internet zijn niet nodig. Coverage meten (eenmalig `dotnet tool install -g dotnet-reportgenerator-globaltool`):

```bash
dotnet test --settings PortfolioApi.Tests/coverage.runsettings --results-directory TestResults
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:"Html;TextSummary"
```

## Commits op de blog

Commits op `main` worden automatisch blogposts: bij het starten van de API, elke 15 minuten en met
`POST /api/admin/sync-commits`. Merge commits worden overgeslagen. Een commitpost heeft een `commitSha` en
een link naar GitHub. Uitzetten kan met `GitHub:SyncEnabled = false` in `appsettings.json`.

## Online

| | Adres |
|---|---|
| Frontend (Netlify) | https://wpfwdesigns.netlify.app (voorlopig privé) |
| API (Azure App Service) | https://wpfwdesigns-api.azurewebsites.net |

Een push naar `main` draait de tests en deployt daarna de API (GitHub Actions) en de frontend (Netlify).
Inrichting en beheer: [DEPLOYMENT.md](DEPLOYMENT.md).
