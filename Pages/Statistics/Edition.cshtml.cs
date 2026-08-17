using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Statistics;

public sealed class EditionModel(IStatisticsOverviewService statistics) : PageModel
{
    public EditionIndividualStatistics Statistics { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var result = await statistics.GetEditionAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Statistics = result;
        return Page();
    }
}
