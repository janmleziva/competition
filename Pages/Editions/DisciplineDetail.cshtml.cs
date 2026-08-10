using System.ComponentModel.DataAnnotations;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DisciplineDetailModel(IDisciplineAdministrationService disciplines) : PageModel
{
    public EditionDisciplineDetail Setup { get; private set; } = null!;

    [BindProperty]
    public EditionDisciplineInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, long disciplineId, CancellationToken ct) =>
        await LoadAsync(id, disciplineId, true, ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostUpdateAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.UpdateAsync(id, disciplineId, Input, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, disciplineId, false, ct) ? Page() : NotFound();
        }

        StatusMessage = "Nastavení disciplíny bylo uloženo.";
        return RedirectToPage(new { id, disciplineId });
    }

    public async Task<IActionResult> OnPostLockAsync(long id, long disciplineId, CancellationToken ct) =>
        await SetLockAsync(id, disciplineId, true, ct);

    public async Task<IActionResult> OnPostUnlockAsync(long id, long disciplineId, CancellationToken ct) =>
        await SetLockAsync(id, disciplineId, false, ct);

    public static string GetPlayingSystemLabel(PlayingSystemType system) => system switch
    {
        PlayingSystemType.RoundRobin => "Každý s každým",
        PlayingSystemType.GroupsThenClassificationMatches => "2 skupiny a poté o umístění",
        PlayingSystemType.Knockout => "Vyřazovací pavouk",
        PlayingSystemType.RoundRobinThenKnockout => "Skupiny a poté pavouk",
        PlayingSystemType.Custom => "Vlastní systém",
        _ => system.ToString()
    };

    private async Task<IActionResult> SetLockAsync(long id, long disciplineId, bool isLocked, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }
        if (!await disciplines.SetLockAsync(id, disciplineId, isLocked, ct))
        {
            return NotFound();
        }

        StatusMessage = isLocked ? "Nastavení disciplíny bylo uzamčeno." : "Nastavení disciplíny bylo odemčeno.";
        return RedirectToPage(new { id, disciplineId });
    }

    private async Task<bool> LoadAsync(long id, long disciplineId, bool populateInput, CancellationToken ct)
    {
        var setup = await disciplines.GetDetailAsync(id, disciplineId, ct);
        if (setup is null)
        {
            return false;
        }

        Setup = setup;
        if (populateInput)
        {
            var item = setup.Discipline;
            Input = new EditionDisciplineInput
            {
                DisciplineId = item.DisciplineId,
                Order = item.Order,
                TeamSize = item.TeamSize,
                PlayingSystem = item.PlayingSystem,
                UsesSetScores = item.UsesSetScores,
                SetsToWin = item.SetsToWin,
                Description = item.Description,
                ScheduledAt = item.ScheduledAt
            };
        }
        return true;
    }
}
