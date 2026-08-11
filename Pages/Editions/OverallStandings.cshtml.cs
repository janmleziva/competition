using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class OverallStandingsModel(ICompetitionScoringService scoring) : PageModel
{
    public EditionOverallStanding Standing { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var standing = await scoring.GetEditionOverallStandingAsync(id, ct);
        if (standing is null) return NotFound();
        Standing = standing;
        return Page();
    }
}
