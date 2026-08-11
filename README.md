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
`Pages`; each page has its own `.cshtml` markup and optional `.cshtml.cs` page model.

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

## Edition administration

Competition editions are listed under `/Editions`. The list and detail pages are public, while create, edit, and active-edition actions require an authenticated administrator:

- create an edition with a name, city, start date, and end date;
- reopen an edition and edit those details;
- select one edition as the explicit active edition.

The first edition is made active automatically. Later active-edition changes are explicit, and
the database permits at most one active edition. Creation forms use a unique submission token,
so retrying the same form submission does not create a duplicate row.

After updating an existing database from Step 1, rerun `scripts/sql/001_initial_schema.sql` in
the Forpsi MSSQL web interface. It adds the Step 2 columns and indexes without recreating or
deleting existing edition data.

## Competitors and registration

The reusable competitor catalogue is available at `/Competitors`. Anonymous visitors can
search and read the catalogue. Authenticated administrators can add competitors and edit their
first name, last name, and optional date of birth.

Each edition detail links to its public registration list at `/Editions/{id}/Competitors`.
Administrators can register catalogue competitors, assign or change a unique positive seed,
and remove registrations. A competitor can appear only once in an edition, and registrations
that are already used by a discipline team cannot be removed. All write handlers enforce
authentication even when called directly.

Step 3 uses the existing `Competitors` and `CompetitionEntries` tables, so it does not require
a new schema migration. The unique database indexes remain the final safeguard for competitor
and seed uniqueness within an edition.

SQL connection behavior is configured under `Database` in `appsettings.json`. The defaults use
a 5-second connection timeout, a 10-second command timeout, and one retry with at most a
1-second delay. These values can be overridden with environment variables such as
`Database__ConnectTimeoutSeconds`.

Run the automated checks with:

```powershell
dotnet test Competition.sln -c Release
```

## Group standings

Public group and round-robin pages show a live standings table for every configured group,
including assigned teams that have not played yet. The table is recalculated from completed
matches on every request, so result edits are visible immediately without a cache or stored
aggregate values.

Teams are ordered by table points; points, score difference, and applicable subscore difference
in a head-to-head mini-table among teams tied on total points; overall score difference;
applicable overall subscore difference; total score (and, where applicable, total set score)
scored; competition seed; and finally team ID.
The phase's configured win/draw/loss values are used (2/1/0 by default).

When completed stages determine final placements, the page also exposes a progressive
`Konečné umístění` view. Knockout teams eliminated in the same stage are ordered by total score
difference, applicable subscore difference, total score and set score scored, and seed.

Generated round-robin schedules keep every team's home and away match counts equal, or at most
one match apart. Team labels use `Surname F.`; when surnames and first-name initials collide, the
first-name prefix is extended only as far as needed to distinguish the participants.

## Award points and overall standings

Reusable global point systems are managed at `/PointSystems` and can also be created or edited
from an edition discipline detail. Each discipline selects its own system. After every match is
completed, an authenticated administrator can confirm finalization from either the discipline
list or phase page. Finalization snapshots each team's rank and points, gives the full point
value to every team member, and permanently closes all discipline mutations.

Finalized points appear in the discipline's `Konečné umístění` table and as a top-four inline
summary on the edition discipline list. `/Editions/{id}/Standings` shows each competitor's
points and team for every discipline plus the total. Equal totals are ordered by the number of
best placements (first-place count, then second-place count, and so on); identical totals and
placement profiles share the same displayed place.
