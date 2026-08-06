using System.Globalization;
using Competition.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Competition.Pages.Admin;

public sealed class CreateCompModel(
    IOptionsMonitor<CompetitionSettings> competitionOptions,
    IWebHostEnvironment environment) : PageModel
{
    public string EnvironmentName => environment.EnvironmentName;

    public string TargetDisplay => competitionOptions.CurrentValue.TargetDate
        .ToString("d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture);

    public void OnGet()
    {
    }
}
