# Competition Tracker

A mobile-first ASP.NET Core Razor Pages app for tracking competitors, disciplines, matches, results, and aggregate standings.

## Proposed platform

- **Runtime:** ASP.NET Core 8 Razor Pages for a simple server-rendered app that works well on mobile browsers.
- **Persistence:** EF Core with SQLite by default for local development. Production can use Microsoft SQL Server by setting `DatabaseProvider=SqlServer` and supplying a SQL Server connection string.
- **Hosting target:** Any paid ASP.NET Core hosting provider that supports .NET 8 app publishing over FTP/SFTP. See [deployment plan](docs/deployment-plan.md) for the recommended FTP + MSSQL release process.
- **Mobile usage:** Responsive CSS with large controls, sticky navigation, and card-based views for quick data entry from a phone.

## Run locally

```bash
dotnet restore
dotnet run --project src/CompetitionTracker
```

Open the shown local URL in a browser. The app creates `competition.db` automatically on first start.

## Production configuration

Set these values in the hosting provider's environment/configuration panel instead of committing secrets:

- `DatabaseProvider=SqlServer`
- `ConnectionStrings__CompetitionDb=<MSSQL connection string>`
- `ASPNETCORE_ENVIRONMENT=Production`

For local SQLite development, no additional settings are required.
