using System.ComponentModel.DataAnnotations;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.PointSystems;

public sealed class IndexModel(IAwardPointSystemService pointSystems) : PageModel
{
    public IReadOnlyList<AwardPointSystemItem> Systems { get; private set; } = [];

    [BindProperty]
    public AwardPointSystemInput Input { get; set; } = new()
    {
        Rules =
        [
            new() { Rank = 1, Points = 6 },
            new() { Rank = 2, Points = 4 },
            new() { Rank = 3, Points = 2 },
            new() { Rank = 4, Points = 1 }
        ]
    };

    [BindProperty]
    public AwardPointSystemInput EditInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool ShowCreate { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? EditId { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct, populateEditInput: true);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            await pointSystems.CreateAsync(Input, ct);
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ShowCreate = true;
            await LoadAsync(ct, populateEditInput: false);
            return Page();
        }
        StatusMessage = "Bodovací systém byl vytvořen.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(long systemId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            if (!await pointSystems.UpdateAsync(systemId, EditInput, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            EditId = systemId;
            await LoadAsync(ct, populateEditInput: false);
            return Page();
        }
        StatusMessage = "Bodovací systém byl uložen.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long systemId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            if (!await pointSystems.DeleteAsync(systemId, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(ct, populateEditInput: false);
            return Page();
        }
        StatusMessage = "Bodovací systém byl smazán.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct, bool populateEditInput)
    {
        Systems = await pointSystems.ListAsync(ct);
        ShowCreate |= Systems.Count == 0;

        if (EditId is null)
        {
            return;
        }

        var system = Systems.SingleOrDefault(x => x.Id == EditId);
        if (system is null || system.IsInUse)
        {
            EditId = null;
            return;
        }

        if (populateEditInput)
        {
            EditInput = new AwardPointSystemInput
            {
                Name = system.Name,
                Rules = system.Rules.Select(rule => new RankingPointRuleInput
                {
                    Rank = rule.Rank,
                    Points = rule.Points
                }).ToList()
            };
        }
    }
}
