using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Competitors;

[Authorize]
public sealed class EditModel(ICompetitorAdministrationService competitors) : PageModel
{
    [BindProperty]
    public CompetitorInput Input { get; set; } = new();

    public long CompetitorId { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var competitor = await competitors.GetAsync(id, cancellationToken);
        if (competitor is null)
        {
            return NotFound();
        }

        CompetitorId = id;
        Input = new CompetitorInput
        {
            FirstName = competitor.FirstName,
            LastName = competitor.LastName,
            DateOfBirth = competitor.DateOfBirth
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        CompetitorId = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await competitors.UpdateAsync(id, Input, cancellationToken))
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Údaje soutěžícího byly upraveny.";
        return RedirectToPage("/Competitors/Index");
    }
}
