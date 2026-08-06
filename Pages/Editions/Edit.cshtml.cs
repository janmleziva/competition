using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

[Authorize]
public sealed class EditModel(IEditionAdministrationService editions) : PageModel
{
    [BindProperty]
    public EditionInput Input { get; set; } = new();

    public long EditionId { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var edition = await editions.GetAsync(id, cancellationToken);
        if (edition is null)
        {
            return NotFound();
        }

        EditionId = id;
        Input = new EditionInput
        {
            Name = edition.Name,
            City = edition.City,
            StartDate = edition.StartDate,
            EndDate = edition.EndDate
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        EditionId = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await editions.UpdateAsync(id, Input, cancellationToken))
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Soutěž byla upravena.";
        return RedirectToPage("/Editions/Details", new { id });
    }
}
