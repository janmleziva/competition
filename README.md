# Competition

Small ASP.NET Core countdown app for the competition landing page.

## Database

The app uses a SQLite database at `App_Data/competition.db` in both local development and
production. It does not require a database server or a production connection string. On first
startup, an empty database is created automatically if the file is missing.

The existing SQL Server LocalDB data can be converted with the included migration tool:

```powershell
dotnet run --project .\Tools\LocalDbToSqlite\LocalDbToSqlite.csproj -- --overwrite
```

By default, the tool reads `(localdb)\MSSQLLocalDB` database `CompetitionDev`, writes
`App_Data/competition.db`, verifies every table's row count, and creates a timestamped backup
before replacing an existing SQLite file. Use `--source` or `--destination` to override those
defaults.

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
`AdminAccess__Password`. Settings changes do not require recompilation; configuration-file
changes are reloaded while the app is running, except cookie lifetime changes, which apply
after an app restart.

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

Interactive deployments pause for a key press after displaying the final outcome, so a window
opened through `deploy.cmd` does not close before the result can be read. Pass `-NoPause` for
automation or when the pause is not wanted. Console output shows when the site goes offline and
comes back online, every file that was uploaded, upload/skip totals, and total deployment time;
the detailed per-file comparison trace remains in the timestamped log.

You can also override the defaults if needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1 `
  -FtpHost "d113wh.forpsi.com" `
  -RemoteDir "/www"
```

Each deployment writes a timestamped log file to `deployment/logs/`. The folder is intentionally ignored by Git so local run logs stay on your machine.

The first deployment uploads the converted `App_Data/competition.db` as the production seed.
Later deployments detect the existing production database, download a timestamped backup into
`deployment/database-backups/`, and leave the live production database untouched. This allows
production data to keep evolving independently without being overwritten by a code deployment.
The deployment briefly takes the application offline so the SQLite backup and application-file
replacement are consistent, then automatically brings it back online after success or rollback.
Files whose content already matches production are skipped. Existing third-party DLLs and files
under `runtimes/` are replaced only when the published file has a strictly newer file version;
when a reliable version comparison is unavailable, the remote file is preserved and the reason
is recorded in the deployment log.

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
