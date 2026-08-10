using System.ComponentModel.DataAnnotations;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Disciplines;

public sealed class IndexModel(IDisciplineAdministrationService disciplines) : PageModel
{
    public IReadOnlyList<DisciplineCatalogItem> Items { get; private set; } = [];
    [BindProperty] public DisciplineCatalogInput Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }
        try { await disciplines.CreateCatalogAsync(Input, cancellationToken); }
        catch (ValidationException ex) { ModelState.AddModelError(string.Empty, ex.Message); await LoadAsync(cancellationToken); return Page(); }
        StatusMessage = "Disciplína byla uložena do katalogu.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(long id, string name, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try { if (!await disciplines.RenameCatalogAsync(id, new DisciplineCatalogInput { Name = name }, cancellationToken)) return NotFound(); }
        catch (ValidationException ex) { ModelState.AddModelError(string.Empty, ex.Message); await LoadAsync(cancellationToken); return Page(); }
        StatusMessage = "Název disciplíny byl změněn.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try { if (!await disciplines.DeleteCatalogAsync(id, cancellationToken)) return NotFound(); }
        catch (ValidationException ex) { ModelState.AddModelError(string.Empty, ex.Message); await LoadAsync(cancellationToken); return Page(); }
        StatusMessage = "Disciplína byla z katalogu odstraněna.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) => Items = await disciplines.ListCatalogAsync(cancellationToken);
}
