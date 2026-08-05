# FTP + MSSQL deployment plan

This plan targets a paid ASP.NET Core hosting service where the web app is uploaded to an FTP/SFTP folder and data is stored in the provider's Microsoft SQL Server instance.

## 1. One-time hosting setup

1. Confirm the hosting plan supports the .NET 8 ASP.NET Core Hosting Bundle.
2. Create or identify the web root FTP folder, for example `/site/wwwroot` or `/wwwroot`.
3. Create a production MSSQL database and user with permissions to create tables, read data, insert data, update data, and delete data.
4. Add these application settings in the host control panel:
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `DatabaseProvider=SqlServer`
   - `ConnectionStrings__CompetitionDb=<production MSSQL connection string>`
5. Ensure the production connection string is never committed to source control.
6. Visit `/health` after each deployment to confirm the application starts successfully.

## 2. Credential model

Store deployment credentials outside the repository as environment variables or CI/CD secrets:

| Variable | Purpose |
| --- | --- |
| `FTP_HOST` | FTP/SFTP host name supplied by the provider. |
| `FTP_USER` | FTP/SFTP username. |
| `FTP_PASSWORD` | FTP/SFTP password or app-specific deployment password. |
| `FTP_REMOTE_PATH` | Remote application folder, such as `/site/wwwroot`. |
| `MSSQL_CONNECTION_STRING` | Production database connection string used only for scripted database checks or migrations. |

## 3. Release process

1. Run tests locally or in CI: `dotnet test CompetitionTracker.sln`.
2. Publish a Release build: `dotnet publish src/CompetitionTracker/CompetitionTracker.csproj -c Release -o ./artifacts/publish`.
3. Back up the MSSQL database from the hosting control panel before replacing production files.
4. Upload the contents of `./artifacts/publish` to `FTP_REMOTE_PATH` using the provider's FTP/SFTP endpoint.
5. Keep production settings in the hosting panel; do not upload `appsettings.Production.json` with secrets.
6. Restart or recycle the site from the hosting panel if the provider does not do so automatically.
7. Verify `/health`, then smoke-test competitors, disciplines, matches, results, and standings pages.

## 4. Scripted deployment

Use `scripts/deploy-ftp.ps1` from a machine with PowerShell 7+ and network access:

```powershell
$env:FTP_HOST = "ftp.example.com"
$env:FTP_USER = "deployment-user"
$env:FTP_PASSWORD = "deployment-password"
$env:FTP_REMOTE_PATH = "/site/wwwroot"
./scripts/deploy-ftp.ps1
```

The script restores dependencies, publishes the app, and recursively uploads the published files to FTP. It intentionally does not store credentials or production connection strings in the repository.

## 5. Database deployment notes

The app currently creates missing tables on startup through Entity Framework Core `EnsureCreated`. For larger production use, replace this with EF Core migrations so schema changes are reviewed and applied explicitly before the web files are swapped.

Recommended migration-based workflow for future schema changes:

1. Add a migration in development.
2. Generate an idempotent SQL script during release.
3. Review and back up production.
4. Run the script against MSSQL using the provider's SQL tooling or a locked-down deployment user.
5. Deploy the web files after the database script succeeds.

## 6. Rollback plan

1. Keep the previous `artifacts/publish` archive for every release.
2. If verification fails, upload the previous archive back to `FTP_REMOTE_PATH`.
3. Restore the MSSQL backup only when a failed release changed data or schema.
4. Re-run `/health` and smoke tests after rollback.
