# Competition

Small ASP.NET Core countdown app for the competition landing page.

## Database

The app uses Entity Framework Core with SQL Server for data access, but the initial schema is
created from a rerunnable SQL script instead of EF migrations. This matches the Forpsi MSSQL
hosting limitation where schema changes are applied through the web SQL interface.

The initial schema script is:

- `scripts/sql/001_initial_schema.sql`

Run that script in the Forpsi MSSQL web interface. It is idempotent, so it can be rerun safely:
existing tables, constraints, and indexes are skipped.

For local development, keep the real connection string in user secrets instead of git-tracked
files:

```powershell
dotnet user-secrets set "ConnectionStrings:CompetitionDb" "Server=YOUR_SERVER;Database=Competition;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
```

Restore packages with:

```powershell
dotnet restore
```

The domain/schema decisions and the incremental feature roadmap are in
[`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md).

## Pages and access

The UI uses Razor Pages. Add anonymous pages directly under `Pages` and admin pages under
`Pages/Admin`; each page has its own `.cshtml` markup and optional `.cshtml.cs` page model.

Access and appearance are configured in `appsettings.json`:

- `AdminAccess.ProtectedPathPrefixes` controls which URL areas require authentication.
- `AdminAccess.Username` and `AdminAccess.Password` define the temporary admin login.
- `AdminAccess.CookieLifetimeHours` controls the login duration.
- `Theme` contains the shared colors, font, and border radius.
- `Competition` contains the countdown title and target date.

Configuration values can also be supplied as environment variables, for example
`AdminAccess__Password` or `ConnectionStrings__CompetitionDb`, so production credentials do
not need to be committed. Settings changes do not require recompilation; configuration-file
changes are reloaded while the app is running, except cookie lifetime changes, which apply
after an app restart.

The application no longer runs `Database.Migrate()` on startup. The SQL schema must already be
present before the app starts using the database.

## Azure publish

1. Open `Competition.sln` in Visual Studio.
2. Sign in to Azure.
3. Right-click the project and choose `Publish`.
4. Select `Azure` and then `Azure App Service`.
5. Create or select your App Service.
6. Publish.

## FTP deployment

Use `scripts/deploy.ps1` to publish and upload the app to the FTP host.

Required local file:

- `deployment/ftp-creds.json`

Defaults used by the script:

- `FTP_HOST = d113wh.forpsi.com`
- `REMOTE_DIR = /www`

### Run it

Create `deployment/ftp-creds.json` with this shape:

```json
{
  "username": "mlezikcz",
  "password": "<your-ftp-password>"
}
```

Then run from the repository root:

```powershell
.\deployment\deploy.cmd
```

If you prefer to call PowerShell directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1
```

You can also override the defaults if needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1 `
  -FtpHost "d113wh.forpsi.com" `
  -RemoteDir "/www"
```

Each deployment writes a timestamped log file to `deployment/logs/`. The folder is intentionally ignored by Git so local run logs stay on your machine.

## Local run

Run the project from Visual Studio or with `dotnet run` from this folder.
