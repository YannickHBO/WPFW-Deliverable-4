# Deployment

| Onderdeel | Waar | Adres |
|---|---|---|
| Frontend | Netlify, project `wpfwdesigns` | https://wpfwdesigns.netlify.app (voorlopig privé) |
| API | Azure App Service `wpfwdesigns-api` (F1, Linux, .NET 10) | https://wpfwdesigns-api.azurewebsites.net |
| Database | Azure SQL `sql-wpfwdesigns` / `PortfolioDb` (Basic) | alleen bereikbaar voor de API |

Netlify stuurt `/api/*` door naar de API (`netlify.toml`), dus de browser praat alleen met het
Netlify-domein. Waarom het zo is opgezet: [ARCHITECTUUR.md, hoofdstuk 9](ARCHITECTUUR.md#9-deployment-azure-en-netlify).

## Nieuwe versie online zetten

- **API:** `.github/workflows/backend.yml` draait bij elke push en pull request de tests. Bij een push naar
  `main` met wijzigingen in de backend deployt hij daarna naar Azure, maar alleen als alle tests slagen.
  Handmatig: Actions → Backend - test en deploy → Run workflow.
- **Frontend:** Netlify bouwt `client/` bij elke push naar `main`.
- Nieuwe migraties voert de API zelf uit bij de eerste start na een deploy.

## Inloggen zonder geheimen

- GitHub logt in bij Azure met OpenID Connect. Er staat geen Azure-wachtwoord in GitHub.
- De API logt in op de database met de Managed Identity van de App Service. De connectionstring bevat geen
  wachtwoord, en de SQL-server staat alleen Microsoft Entra ID-login toe.

Instellingen in GitHub:

| Waar | Naam | Waarde |
|---|---|---|
| Environment `production` (alleen `main`), secret | `AZURE_CLIENT_ID` | client-id van `id-github-deploy` |
| idem | `AZURE_TENANT_ID` | tenant-id |
| idem | `AZURE_SUBSCRIPTION_ID` | abonnements-id |
| Actions, variable | `AZURE_WEBAPP_NAME` | `wpfwdesigns-api` |

## Azure inrichten

Met de Azure CLI (`az login`). Alleen de database kost geld, ongeveer €4,50 per maand. In Cost Management
staat een budget van €5 per maand met een mail bij 80 % en 100 %.

Let op:
- Het studentenabonnement staat West Europe niet toe. Alles staat in `belgiumcentral`.
- In een nieuw abonnement eerst `az provider register --namespace Microsoft.Sql` en `Microsoft.Web`.
  Zonder dat geeft SQL de fout `SubscriptionNotFound`.
- GitHub zet de nummers van account en repository in het OIDC-subject. Het subject hieronder moet daar
  letterlijk mee overeenkomen, anders faalt het inloggen met `AADSTS700213`.

```powershell
az group create --name rg-portfolio --location westeurope

# SQL-server (alleen Entra ID-login) en database
$me = az ad signed-in-user show --query "{upn:userPrincipalName, id:id}" -o json | ConvertFrom-Json
az sql server create --name sql-wpfwdesigns --resource-group rg-portfolio --location belgiumcentral --enable-ad-only-auth --external-admin-principal-type User --external-admin-name $me.upn --external-admin-sid $me.id
az sql server firewall-rule create --resource-group rg-portfolio --server sql-wpfwdesigns --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az sql db create --resource-group rg-portfolio --server sql-wpfwdesigns --name PortfolioDb --edition Basic --capacity 5 --max-size 2GB --backup-storage-redundancy Local

# App Service
az appservice plan create --name asp-portfolio --resource-group rg-portfolio --location belgiumcentral --sku F1 --is-linux
az webapp create --name wpfwdesigns-api --resource-group rg-portfolio --plan asp-portfolio --runtime "DOTNETCORE:10.0" --https-only true
az webapp config set --name wpfwdesigns-api --resource-group rg-portfolio --min-tls-version 1.2 --ftps-state Disabled
az resource update --resource-group rg-portfolio --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/wpfwdesigns-api --name scm --set properties.allow=false
az resource update --resource-group rg-portfolio --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/wpfwdesigns-api --name ftp --set properties.allow=false
az webapp config appsettings set --name wpfwdesigns-api --resource-group rg-portfolio --settings ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
az webapp config connection-string set --name wpfwdesigns-api --resource-group rg-portfolio --connection-string-type SQLAzure --settings PortfolioDb="Server=tcp:sql-wpfwdesigns.database.windows.net,1433;Database=PortfolioDb;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
az webapp identity assign --name wpfwdesigns-api --resource-group rg-portfolio

# Identiteit voor GitHub Actions, alleen rechten op deze Web App
az identity create --name id-github-deploy --resource-group rg-portfolio --location belgiumcentral
az identity federated-credential create --name github-production --identity-name id-github-deploy --resource-group rg-portfolio --issuer https://token.actions.githubusercontent.com --subject "repo:YannickHBO@114572375/WPFW-Deliverable-4@1474379175:environment:production" --audiences api://AzureADTokenExchange
$principalId = az identity show --name id-github-deploy --resource-group rg-portfolio --query principalId -o tsv
$webappId = az webapp show --name wpfwdesigns-api --resource-group rg-portfolio --query id -o tsv
az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "Website Contributor" --scope $webappId
```

Rechten van de API in de database, uitvoeren in de Query editor van de Azure Portal (database `PortfolioDb`,
ingelogd als serverbeheerder):

```sql
CREATE USER [wpfwdesigns-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [wpfwdesigns-api];
ALTER ROLE db_datawriter ADD MEMBER [wpfwdesigns-api];
ALTER ROLE db_ddladmin  ADD MEMBER [wpfwdesigns-api];  -- voor de migraties bij het starten
```

## Eerste Admin online

1. Registreren via de API (het wachtwoord gaat via een venster en niet in de terminalgeschiedenis):

   ```powershell
   $cred = Get-Credential -Message "E-mailadres en wachtwoord"
   $body = @{ email = $cred.UserName; password = $cred.GetNetworkCredential().Password } | ConvertTo-Json
   Invoke-RestMethod -Method Post -Uri https://wpfwdesigns-api.azurewebsites.net/api/auth/register -ContentType "application/json" -Body $body
   Remove-Variable cred, body
   ```

2. `scripts/assign-role.sql` uitvoeren in de Query editor. Plak het script vanaf `SET QUOTED_IDENTIFIER ON;`
   en vervang in de editor `$(Email)` en `$(Role)` door het e-mailadres en `Admin`. De lokale `sqlcmd` kan
   niet met een Microsoft-account op Azure SQL inloggen.
3. De firewallregel die de Query editor voor je IP-adres aanmaakt, daarna weer verwijderen:

   ```powershell
   az sql server firewall-rule list --resource-group rg-portfolio --server sql-wpfwdesigns --query "[].name" -o tsv
   az sql server firewall-rule delete --resource-group rg-portfolio --server sql-wpfwdesigns --name <naam>
   ```

## Problemen zoeken

```powershell
az webapp log config --name wpfwdesigns-api --resource-group rg-portfolio --docker-container-logging filesystem
az webapp log tail --name wpfwdesigns-api --resource-group rg-portfolio
```

Na een tijd zonder bezoekers duurt de eerste aanroep 10 tot 30 seconden, omdat de app op het F1-plan dan
opnieuw opstart. Swagger staat in productie uit.

## Alles verwijderen

```powershell
az group delete --name rg-portfolio
```

Het Netlify-project verwijder je in het Netlify-dashboard.
