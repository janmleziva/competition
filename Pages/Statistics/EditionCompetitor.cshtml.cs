using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Statistics;

public sealed class EditionCompetitorModel(IStatisticsOverviewService statistics) : PageModel
{
    public EditionCompetitorResults Results { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(
        long id,
        long competitorId,
        CancellationToken cancellationToken)
    {
        var result = await statistics.GetEditionCompetitorResultsAsync(id, competitorId, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Results = result;
        return Page();
    }
}
