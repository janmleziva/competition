using System.Globalization;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class IndexModel(
    IEditionAdministrationService editions,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<IndexModel> logger) : PageModel
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    public IReadOnlyList<EditionSummary> Editions { get; private set; } = [];
    public string? DatabaseError { get; private set; }
    public string? ConnectionString { get; private set; }
    public bool ShowFullConnectionString { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Editions = await editions.ListAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Nepodařilo se načíst soutěže z databáze.");
            DatabaseError = exception.GetBaseException().Message;
            ShowFullConnectionString = environment.IsDevelopment() &&
                configuration.GetValue<bool>("DatabaseDiagnostics:ShowFullConnectionString");
            ConnectionString = ShowFullConnectionString
                ? configuration.GetConnectionString("CompetitionDb")
                : null;
        }
    }

    public async Task<IActionResult> OnPostSetActiveAsync(long id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        if (!await editions.SetActiveAsync(id, cancellationToken))
        {
            return NotFound();
        }

        StatusMessage = "Aktivní soutěž byla změněna.";
        return RedirectToPage();
    }

    public string FormatDateRange(DateOnly startDate, DateOnly endDate) =>
        $"{startDate.ToString("d. MMM yyyy", CzechCulture)} - {endDate.ToString("d. MMM yyyy", CzechCulture)}";
}
