using System.ComponentModel.DataAnnotations;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DisciplineDetailModel(
    IDisciplineAdministrationService disciplines,
    ICompetitionScoringService scoring,
    IAwardPointSystemService pointSystems) : PageModel
{
    public EditionDisciplineDetail Setup { get; private set; } = null!;
    public DisciplineScoringSetup Scoring { get; private set; } = null!;

    [BindProperty]
    public EditionDisciplineInput Input { get; set; } = new();

    [BindProperty]
    public long? AwardPointSystemId { get; set; }

    [BindProperty]
    public AwardPointSystemInput NewPointSystem { get; set; } = CreateDefaultPointSystem();

    [BindProperty]
    public AwardPointSystemInput EditPointSystem { get; set; } = new();

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

    public async Task<IActionResult> OnPostSetPointSystemAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            if (!await scoring.SetPointSystemAsync(id, disciplineId, AwardPointSystemId, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, disciplineId, false, ct) ? Page() : NotFound();
        }
        StatusMessage = "Bodovací systém disciplíny byl uložen.";
        return RedirectToPage(new { id, disciplineId });
    }

    public async Task<IActionResult> OnPostCreatePointSystemAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            if (!await EnsurePointSystemEditableAsync(id, disciplineId, ct)) return NotFound();
            var systemId = await pointSystems.CreateAsync(NewPointSystem, ct);
            if (!await scoring.SetPointSystemAsync(id, disciplineId, systemId, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, disciplineId, false, ct) ? Page() : NotFound();
        }
        StatusMessage = "Bodovací systém byl vytvořen a přiřazen disciplíně.";
        return RedirectToPage(new { id, disciplineId });
    }

    public async Task<IActionResult> OnPostUpdatePointSystemAsync(long id, long disciplineId, long systemId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        try
        {
            if (!await EnsurePointSystemEditableAsync(id, disciplineId, ct)) return NotFound();
            var currentScoring = await scoring.GetDisciplineSetupAsync(id, disciplineId, ct);
            if (currentScoring is null || currentScoring.AwardPointSystemId != systemId) return NotFound();
            var copyId = await pointSystems.CreateCopyAsync(systemId, EditPointSystem, ct);
            if (copyId is null || !await scoring.SetPointSystemAsync(id, disciplineId, copyId, ct)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, disciplineId, false, ct) ? Page() : NotFound();
        }
        StatusMessage = "Pro disciplínu byla vytvořena a přiřazena nová kopie bodovacího systému.";
        return RedirectToPage(new { id, disciplineId });
    }

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
        Scoring = await scoring.GetDisciplineSetupAsync(id, disciplineId, ct) ?? throw new InvalidOperationException();
        AwardPointSystemId = Scoring.AwardPointSystemId;
        if ((populateInput || EditPointSystem.Rules.Count == 0) && Scoring.AwardPointSystemId is not null)
        {
            var currentSystem = Scoring.AvailableSystems.Single(x => x.Id == Scoring.AwardPointSystemId);
            EditPointSystem = new AwardPointSystemInput
            {
                Name = currentSystem.Name,
                Rules = currentSystem.Rules.Select(x => new RankingPointRuleInput
                {
                    Rank = x.Rank,
                    Points = x.Points
                }).ToList()
            };
        }
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

    private async Task<bool> EnsurePointSystemEditableAsync(long id, long disciplineId, CancellationToken ct)
    {
        var setup = await disciplines.GetDetailAsync(id, disciplineId, ct);
        if (setup is null)
        {
            return false;
        }
        if (setup.Discipline.IsClosed)
        {
            throw new ValidationException("Uzavřenou disciplínu už nelze měnit.");
        }
        if (setup.Discipline.IsLocked)
        {
            throw new ValidationException("Bodovací systém nelze změnit, dokud je disciplína uzamčená.");
        }
        return true;
    }

    private static AwardPointSystemInput CreateDefaultPointSystem() => new()
    {
        Rules =
        [
            new() { Rank = 1, Points = 6 },
            new() { Rank = 2, Points = 4 },
            new() { Rank = 3, Points = 2 },
            new() { Rank = 4, Points = 1 }
        ]
    };
}
