using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DisciplineTeamResultsModel(IStatisticsOverviewService statistics) : PageModel
{
    public DisciplineTeamResults Results { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(
        long id,
        long disciplineId,
        long teamId,
        CancellationToken cancellationToken)
    {
        var result = await statistics.GetDisciplineTeamResultsAsync(
            id, disciplineId, teamId, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        Results = result;
        return Page();
    }
}
