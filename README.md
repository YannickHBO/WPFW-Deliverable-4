## Techniek

- **.NET-versie:** .NET 10 (`net10.0`, getest met SDK 10.0.403)
- **Packages:** Entity Framework Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`, `.Design`), ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`), Swashbuckle.AspNetCore 10 (Swagger UI)
- **Database:** SQL Server via EF Core met een eigen `PortfolioDbContext` (portfolio-inhoud en gebruikersaccounts)
- **Solution:** `PortfolioApi.slnx` in de root

## API-stijl en codestructuur

De API gebruikt **controllers** (`[ApiController]`) en is gelaagd opgebouwd: Controllers → Services → Data.
De motivatie achter deze keuzes staat in [ARCHITECTUUR.md](ARCHITECTUUR.md).

```
PortfolioApi/
├── Controllers/   ProjectsController, BlogPostsController, AuthController (HTTP)
├── Services/      IProjectService/ProjectService, IBlogPostService/BlogPostService (logica)
├── Auth/          RoleNames (Gast, Editor, Admin) en Policies (CanEditContent)
├── Mappings/      Entity <-> DTO conversie (ToDto, ToEntity, ApplyTo)
├── Dtos/          Invoer- en uitvoer-DTO's met validatie
├── Models/        EF Core-entiteiten: Project, BlogPost, BlogPostImage, User
├── Data/          PortfolioDbContext (Identity-tabellen + seed data)
├── Migrations/    EF Core-migraties
└── Program.cs     Dependency injection, Identity, cookie, autorisatie, Swagger, pipeline
scripts/
└── assign-role.sql   Lokaal een rol (Editor/Admin) toekennen
```

## SQL Server configureren

De connectionstring staat in `PortfolioApi/appsettings.json` onder `ConnectionStrings:PortfolioDb`:

```json
"PortfolioDb": "Server=.\\SQLEXPRESS;Database=PortfolioDb;Trusted_Connection=True;TrustServerCertificate=True"
```

- Standaard wordt een lokale **SQL Server Express**-instantie (`.\SQLEXPRESS`) gebruikt met Windows-authenticatie.
  Er staat dus geen wachtwoord in de connectionstring.
- Gebruik je **LocalDB**, vervang dan `Server=.\\SQLEXPRESS` door `Server=(localdb)\\MSSQLLocalDB`.
- Gebruik je een SQL-login met wachtwoord, zet de connectionstring dan **niet** in `appsettings.json`
  maar in de user-secrets (die staan buiten de repository):

  ```bash
  cd PortfolioApi
  dotnet user-secrets init
  dotnet user-secrets set "ConnectionStrings:PortfolioDb" "Server=...;Database=PortfolioDb;User Id=...;Password=...;TrustServerCertificate=True"
  ```

## Database aanmaken

De database wordt aangemaakt met **EF Core-migraties** (niet met `EnsureCreated`).

- **Automatisch:** bij het starten roept `Program.cs` `db.Database.Migrate()` aan. De database
  `PortfolioDb`, de tabellen en de seed data worden dan aangemaakt of bijgewerkt. Dat geldt ook voor de
  **Identity-migraties**: de tabellen `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles` (en een paar meer)
  en de drie rollen **Gast**, **Editor** en **Admin**.
- **Handmatig** (optioneel):

  ```bash
  dotnet tool install --global dotnet-ef --version 10.0.14
  cd PortfolioApi
  dotnet ef database update
  ```

Omdat de gegevens in SQL Server staan, blijven ze bestaan na een herstart van de applicatie.

## Applicatie starten

```bash
cd PortfolioApi
dotnet run --launch-profile http
```

Open daarna **Swagger UI** op http://localhost:5080/swagger om alle endpoints te testen.

## Gebruikers, rollen en toegang

Accounts worden beheerd met **ASP.NET Core Identity**. Na een geslaagde login zet de API een
authenticatiecookie (`PortfolioAuth`: `HttpOnly`, `Secure`, `SameSite=Lax`). De browser stuurt die
cookie daarna automatisch mee, ook vanuit Swagger UI en straks vanuit de React-app.

| Rol | Wie | Rechten |
|---|---|---|
| **Gast** | Iedere nieuwe registratie | Lezen (zoals iedereen) |
| **Editor** | Handmatig toegekend (script) | Projecten en blogposts aanmaken, wijzigen en verwijderen |
| **Admin** | Handmatig toegekend (script) | Hetzelfde als Editor: Admin staat expliciet in de policy `CanEditContent` |

### Routes

| Route | Toegang | Resultaat |
|---|---|---|
| `GET /api/projects`, `GET /api/projects/{id}` | Iedereen | 200, of 404 bij onbekend id |
| `POST /api/projects` | Editor, Admin | 201, 400 bij ongeldige invoer |
| `PUT /api/projects/{id}` | Editor, Admin | 200, 400 of 404 |
| `DELETE /api/projects/{id}` | Editor, Admin | 204 of 404 |
| `GET /api/blogposts`, `GET /api/blogposts/{id}` | Iedereen | 200, of 404 bij onbekend id |
| `POST /api/blogposts` | Editor, Admin | 201, 400 bij ongeldige invoer |
| `PUT /api/blogposts/{id}` | Editor, Admin | 200, 400 of 404 |
| `DELETE /api/blogposts/{id}` | Editor, Admin | 204 of 404 |
| `POST /api/auth/register` | Iedereen | 201 met rol Gast, 400 bij ongeldige invoer of bestaand e-mailadres |
| `POST /api/auth/login` | Iedereen | 200 + cookie, 401 bij onjuiste gegevens |
| `POST /api/auth/logout` | Iedereen | 204, cookie verwijderd |
| `GET /api/auth/me` | Aangemeld | 200 met e-mail en rollen, anders 401 |

Een schrijfverzoek **zonder geldige aanmelding** krijgt **401**, een aangemelde gebruiker **zonder Editor- of
Admin-rol** krijgt **403**. De API controleert dit zelf met `[Authorize(Policy = Policies.CanEditContent)]`
op elke schrijfactie. Na **3 foute wachtwoorden** achter elkaar is een account 5 minuten geblokkeerd.

### Een gewone gebruiker aanmaken

1. Start de API en open http://localhost:5080/swagger.
2. `POST /api/auth/register` met bijvoorbeeld:

   ```json
   { "email": "gast@example.test", "password": "<eigen wachtwoord>" }
   ```

   Het wachtwoord moet minimaal 6 tekens hebben, met een hoofdletter, een kleine letter, een cijfer en
   een speciaal teken (zoals `-` of `!`). De gebruiker krijgt de rol **Gast**. Registratie kent nooit Editor of Admin toe.
3. `POST /api/auth/login` met dezelfde gegevens. De response zet de cookie; `GET /api/auth/me` laat
   zien wie er is aangemeld.

### Editor of Admin toekennen

Editor en Admin worden lokaal toegekend met [scripts/assign-role.sql](scripts/assign-role.sql).
Registreer de gebruiker eerst (zie hierboven) en voer daarna vanuit de root van de repository uit:

```bash
sqlcmd -S .\SQLEXPRESS -E -C -b -W -d PortfolioDb -i scripts/assign-role.sql -v Email="editor@example.test" Role="Editor"
sqlcmd -S .\SQLEXPRESS -E -C -b -W -d PortfolioDb -i scripts/assign-role.sql -v Email="admin@example.test" Role="Admin"
```

In SSMS of Azure Data Studio kan het ook: zet **SQLCMD Mode** aan (Query → SQLCMD Mode) en zet bovenaan
`:setvar Email "editor@example.test"` en `:setvar Role "Editor"`.

Het script controleert of de gebruiker en de rol bestaan, kent de rol toe en geeft de gebruiker een
nieuwe **SecurityStamp**, in één transactie.

### Opnieuw inloggen na een rolwijziging

De API vergelijkt bij elk verzoek de SecurityStamp in de cookie met die in de database. Omdat het script
de stamp vernieuwt, werkt de oude cookie direct niet meer (**401**). De gebruiker logt opnieuw in en
krijgt dan een cookie met de nieuwe rol. Zo blijft een cookie nooit geldig met rechten die niet meer kloppen.

### Wachtwoorden en sleutels

- Er staan **geen wachtwoorden of sleutels** in de repository. Testgebruikers maak je lokaal aan via
  registratie, en hun wachtwoord staat alleen als hash (`AspNetUsers.PasswordHash`) in je lokale database.
- De cookie wordt versleuteld met ASP.NET Core Data Protection. Lokaal staan die sleutels in je
  Windows-profiel (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`), niet in de repository.

## Frontend starten

```bash
cd client
npm install
npm run dev
```
