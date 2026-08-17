using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Statistics;

public sealed class DisciplinesModel(IStatisticsOverviewService statistics) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? DisciplineId { get; set; }

    public DisciplineStatisticsOverview Statistics { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Statistics = await statistics.GetDisciplinesAsync(DisciplineId, cancellationToken);
    }
}
