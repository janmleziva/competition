using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Competitors;

[Authorize]
public sealed class CreateModel(ICompetitorAdministrationService competitors) : PageModel
{
    [BindProperty]
    public CompetitorInput Input { get; set; } = new();

    [BindProperty]
    public bool AllowDuplicateName { get; set; }

    public bool ShowDuplicateConfirmation { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await competitors.CreateAsync(Input, AllowDuplicateName, cancellationToken);
        }
        catch (DuplicateCompetitorException)
        {
            ShowDuplicateConfirmation = true;
            return Page();
        }

        TempData["StatusMessage"] = "Soutěžící byl přidán do seznamu.";
        return RedirectToPage("/Competitors/Index");
    }
}
