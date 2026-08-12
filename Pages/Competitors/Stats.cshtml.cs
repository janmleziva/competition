using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Competitors;

public sealed class StatsModel(ICompetitorStatisticsService statistics) : PageModel
{
    public CompetitorStatistics Statistics { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var result = await statistics.GetAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Statistics = result;
        return Page();
    }
}
