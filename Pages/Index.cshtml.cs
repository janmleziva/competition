using System.Globalization;
using Competition.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Competition.Pages;

public sealed class IndexModel(
    IOptionsMonitor<CompetitionSettings> competitionOptions,
    IWebHostEnvironment environment) : PageModel
{
    public CompetitionSettings Settings { get; private set; } = new();

    public string EnvironmentName => environment.EnvironmentName;

    public string TargetDisplay => Settings.TargetDate.ToString("d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture);

    public void OnGet()
    {
        Settings = competitionOptions.CurrentValue;
    }
}
