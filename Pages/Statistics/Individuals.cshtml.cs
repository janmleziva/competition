using Competition.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Statistics;

public sealed class IndividualsModel(IStatisticsOverviewService statistics) : PageModel
{
    public IndividualStatisticsOverview Statistics { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Statistics = await statistics.GetIndividualsAsync(cancellationToken);
    }
}
