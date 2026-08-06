using System.Globalization;
using Competition.Configuration;
using Competition.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Competition.Pages;

public sealed class IndexModel(
    IOptionsMonitor<CompetitionSettings> competitionOptions,
    IWebHostEnvironment environment,
    IEditionAdministrationService editions,
    ILogger<IndexModel> logger) : PageModel
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly TimeZoneInfo PragueTimeZone = GetPragueTimeZone();

    public CompetitionSettings Settings { get; private set; } = new();

    public string EnvironmentName => environment.EnvironmentName;

    public HomeEditionDisplay? Edition { get; private set; }

    public bool IsCompetitionRunning { get; private set; }

    public DateTimeOffset CountdownTarget { get; private set; }

    public string Heading =>
        IsCompetitionRunning ? "Soutěž právě probíhá" : "Soutěž brzy začíná";

    public string Lead =>
        Edition is null
            ? "Živý odpočet do začátku soutěže."
            : IsCompetitionRunning
                ? "Aktuální aktivní soutěž je právě v běhu."
                : string.Empty;

    public string TargetDisplay =>
        CountdownTarget.ToString("d. MMMM yyyy, H:mm", CzechCulture);

    public string EditionDateDisplay =>
        Edition is null
            ? TargetDisplay
            : $"{FormatDate(Edition.StartDate)} - {FormatDate(Edition.EndDate)}";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Settings = competitionOptions.CurrentValue;
        CountdownTarget = Settings.TargetDate;

        try
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, PragueTimeZone).DateTime);
            var allEditions = await editions.ListAsync(cancellationToken);
            var currentActiveEdition = allEditions
                .FirstOrDefault(x => x.IsActive && x.StartDate <= today && x.EndDate >= today);

            if (currentActiveEdition is not null)
            {
                Edition = HomeEditionDisplay.FromSummary(currentActiveEdition);
                IsCompetitionRunning = true;
                CountdownTarget = BuildTargetDate(Edition.StartDate);
                return;
            }

            var nearestEdition = allEditions
                .Where(x => x.StartDate >= today)
                .OrderBy(x => x.StartDate)
                .ThenBy(x => x.Name)
                .FirstOrDefault();

            if (nearestEdition is not null)
            {
                Edition = HomeEditionDisplay.FromSummary(nearestEdition);
                CountdownTarget = BuildTargetDate(Edition.StartDate);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Nepodařilo se načíst soutěž pro veřejnou úvodní stránku.");
        }
    }

    private string FormatDate(DateOnly date) =>
        date.ToString("d. MMMM yyyy", CzechCulture);

    private DateTimeOffset BuildTargetDate(DateOnly date)
    {
        var localDateTime = date.ToDateTime(TimeOnly.FromTimeSpan(Settings.TargetDate.TimeOfDay));
        return new DateTimeOffset(localDateTime, PragueTimeZone.GetUtcOffset(localDateTime));
    }

    private static TimeZoneInfo GetPragueTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        }
    }
}

public sealed record HomeEditionDisplay(
    long Id,
    string Name,
    string City,
    DateOnly StartDate,
    DateOnly EndDate)
{
    public static HomeEditionDisplay FromSummary(EditionSummary edition) =>
        new(edition.Id, edition.Name, edition.City, edition.StartDate, edition.EndDate);
}
