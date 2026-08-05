# Competition Tracker

A mobile-first ASP.NET Core Razor Pages app for tracking competitors, disciplines, matches, results, and aggregate standings.

## Proposed platform

- **Runtime:** ASP.NET Core 8 Razor Pages for a simple server-rendered app that works well on mobile browsers.
- **Persistence:** EF Core with SQLite by default, suitable for small competitions and easy local backups.
- **Hosting target:** Azure App Service or any Linux container host that supports .NET 8. The SQLite database can live on persistent app storage for small events; PostgreSQL can replace it later if multi-user scale grows.
- **Mobile usage:** Responsive CSS with large controls, sticky navigation, and card-based views for quick data entry from a phone.

## Run locally

```bash
dotnet restore
dotnet run --project src/CompetitionTracker
```

Open the shown local URL in a browser. The app creates `competition.db` automatically on first start.
