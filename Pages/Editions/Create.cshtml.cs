using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

[Authorize]
public sealed class CreateModel(IEditionAdministrationService editions) : PageModel
{
    [BindProperty]
    public EditionInput Input { get; set; } = new();

    [BindProperty]
    public Guid CreationToken { get; set; }

    public void OnGet()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Input.StartDate = today;
        Input.EndDate = today;
        CreationToken = Guid.NewGuid();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (CreationToken == Guid.Empty)
        {
            ModelState.AddModelError(string.Empty, "Platnost formuláře vypršela. Odešlete jej prosím znovu.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var editionId = await editions.CreateAsync(Input, CreationToken, cancellationToken);
        TempData["StatusMessage"] = "Soutěž byla vytvořena.";
        return RedirectToPage("/Editions/Details", new { id = editionId });
    }
}
