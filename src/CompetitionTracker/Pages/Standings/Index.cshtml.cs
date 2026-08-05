using CompetitionTracker.Models;
using CompetitionTracker.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CompetitionTracker.Pages.Standings;

public class IndexModel(StandingsService standingsService) : PageModel
{
    public IReadOnlyList<StandingRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await standingsService.GetStandingsAsync(cancellationToken);
    }
}
