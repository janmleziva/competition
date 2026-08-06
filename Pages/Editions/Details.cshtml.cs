using System.Globalization;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DetailsModel(IEditionAdministrationService editions) : PageModel
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    public EditionDetails Edition { get; private set; } = null!;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var edition = await editions.GetAsync(id, cancellationToken);
        if (edition is null)
        {
            return NotFound();
        }

        Edition = edition;
        return Page();
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
        return RedirectToPage(new { id });
    }

    public string FormatDate(DateOnly date) =>
        date.ToString("d. MMMM yyyy", CzechCulture);
}
