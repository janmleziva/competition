using System.ComponentModel.DataAnnotations;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DisciplinesModel(
    IDisciplineAdministrationService disciplines,
    ICompetitionScoringService? scoring = null) : PageModel
{
    private static readonly IReadOnlyDictionary<PlayingSystemType, string> PlayingSystemLabels =
        new Dictionary<PlayingSystemType, string>
        {
            [PlayingSystemType.RoundRobin] = "Každý s každým",
            [PlayingSystemType.GroupsThenClassificationMatches] = "2 skupiny a poté o umístění",
            [PlayingSystemType.Knockout] = "Vyřazovací pavouk",
            [PlayingSystemType.RoundRobinThenKnockout] = "Skupiny a poté pavouk",
            [PlayingSystemType.Custom] = "Vlastní systém"
        };

    public EditionDisciplineSetup Setup { get; private set; } = null!;

    [BindProperty]
    public EditionDisciplineInput Input { get; set; } = new()
    {
        Order = 0,
        TeamSize = 2,
        SetsToWin = 2
    };

    [BindProperty]
    public DisciplineCatalogInput NewDiscipline { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct) =>
        await LoadAsync(id, ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAttachAsync(long id, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        RemoveModelStatePrefix(nameof(NewDiscipline));
        if (!IsModelStatePrefixValid(nameof(Input)))
        {
            return await ReloadAsync(id, ct);
        }

        try
        {
            await disciplines.AttachAsync(id, Input, ct);
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, ct);
        }

        StatusMessage = "Disciplína byla přiřazena k ročníku.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCreateAndSelectAsync(long id, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        RemoveModelStatePrefix(nameof(Input));

        try
        {
            var disciplineId = await disciplines.CreateCatalogAsync(NewDiscipline, ct);
            Input.DisciplineId = disciplineId;
            NewDiscipline = new DisciplineCatalogInput();
            RemoveModelStatePrefix(nameof(NewDiscipline));
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, ct);
        }

        StatusMessage = "Disciplína byla vytvořena a je připravena k přiřazení.";
        return await ReloadAsync(id, ct);
    }

    public async Task<IActionResult> OnPostUpdateAsync(long id, long disciplineId, EditionDisciplineInput input, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.UpdateAsync(id, disciplineId, input, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, ct);
        }

        StatusMessage = "Nastavení disciplíny bylo uloženo.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.RemoveAsync(id, disciplineId, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, ct);
        }

        StatusMessage = "Disciplína byla z ročníku odebrána.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLockDisciplineAsync(long id, long disciplineId, CancellationToken ct) =>
        await SetLockAsync(id, disciplineId, true, ct);

    public async Task<IActionResult> OnPostUnlockDisciplineAsync(long id, long disciplineId, CancellationToken ct) =>
        await SetLockAsync(id, disciplineId, false, ct);

    public async Task<IActionResult> OnPostFinalizeAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (scoring is null) throw new InvalidOperationException("Služba bodování není dostupná.");
        try
        {
            if (!await scoring.FinalizeDisciplineAsync(id, disciplineId, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, ct);
        }
        StatusMessage = "Body byly přiděleny a disciplína byla uzavřena.";
        return RedirectToPage(new { id });
    }

    public string GetPlayingSystemLabel(PlayingSystemType system) =>
        PlayingSystemLabels.TryGetValue(system, out var label) ? label : system.ToString();

    private async Task<IActionResult> ReloadAsync(long id, CancellationToken ct) =>
        await LoadAsync(id, ct) ? Page() : NotFound();

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
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(long id, CancellationToken ct)
    {
        var setup = await disciplines.GetEditionSetupAsync(id, ct);
        if (setup is null)
        {
            return false;
        }

        Setup = setup;

        if (Input.Order <= 0)
        {
            Input.Order = FindLowestFreeOrder(setup.Disciplines.Select(x => x.Order));
        }

        return true;
    }

    private static int FindLowestFreeOrder(IEnumerable<int> orders)
    {
        var used = orders.Where(x => x > 0).ToHashSet();
        var current = 1;

        while (used.Contains(current))
        {
            current++;
        }

        return current;
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys
            .Where(x => x.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                x.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            ModelState.Remove(key);
        }
    }

    private bool IsModelStatePrefixValid(string prefix) =>
        ModelState
            .Where(x => x.Key.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                x.Key.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase))
            .All(x => x.Value is not null &&
                x.Value.ValidationState != Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid &&
                x.Value.Errors.Count == 0);
}
