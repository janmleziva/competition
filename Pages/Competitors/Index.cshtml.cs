using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Competitors;

public sealed class IndexModel(ICompetitorAdministrationService competitors) : PageModel
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<CompetitorSummary> Competitors { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Competitors = await competitors.SearchAsync(Search, cancellationToken);

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await competitors.DeleteAsync(id, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            return RedirectToPage(new { Search });
        }

        StatusMessage = "Soutěžící byl smazán.";
        return RedirectToPage(new { Search });
    }

    public string FormatDate(DateOnly? date) =>
        date?.ToString("d. MMMM yyyy", CzechCulture) ?? "Neuvedeno";
}
