using System.ComponentModel.DataAnnotations;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class DisciplineParticipantsModel(IDisciplineAdministrationService disciplines) : PageModel
{
    public DisciplineParticipantSetup Setup { get; private set; } = null!;

    [BindProperty]
    public List<long> NewTeamEntryIds { get; set; } = [];

    [BindProperty]
    public int NewTeamVisibleSlots { get; set; } = 1;

    public int? FocusTeamMemberSlot { get; private set; }

    [TempData]
    public string? ParticipantStatusMessage { get; set; }

    [TempData]
    public string? ParticipantErrorMessage { get; set; }

    [TempData]
    public string? TeamStatusMessage { get; set; }

    public bool IsNewTeamComplete =>
        Setup.Discipline.TeamSize > 1 &&
        NewTeamVisibleSlots == Setup.Discipline.TeamSize &&
        NewTeamEntryIds.Take(Setup.Discipline.TeamSize).All(x => x > 0) &&
        NewTeamEntryIds.Take(Setup.Discipline.TeamSize).Distinct().Count() == Setup.Discipline.TeamSize;

    public async Task<IActionResult> OnGetAsync(long id, long disciplineId, CancellationToken ct) =>
        await LoadAsync(id, disciplineId, ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAssignAllAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.AssignAllAsync(id, disciplineId, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ParticipantErrorMessage = ex.Message;
            return RedirectToAnchor(id, disciplineId, "participants-heading");
        }

        ParticipantStatusMessage = "Všichni soutěžící byli přiřazeni do disciplíny.";
        return RedirectToAnchor(id, disciplineId, "participants-heading");
    }

    public async Task<IActionResult> OnPostToggleParticipantAsync(
        long id,
        long disciplineId,
        long entryId,
        bool isAssigned,
        CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var setup = await disciplines.GetParticipantSetupAsync(id, disciplineId, ct);
        if (setup is null || setup.Participants.All(x => x.EntryId != entryId))
        {
            return NotFound();
        }

        var assignedIds = setup.Participants
            .Where(x => x.IsAssigned)
            .Select(x => x.EntryId)
            .ToHashSet();

        if (isAssigned)
        {
            assignedIds.Add(entryId);
        }
        else
        {
            assignedIds.Remove(entryId);
        }

        try
        {
            if (!await disciplines.UpdateParticipantsAsync(id, disciplineId, assignedIds, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ParticipantErrorMessage = ex.Message;
            return RedirectToAnchor(id, disciplineId, $"participant-{entryId}");
        }

        ParticipantStatusMessage = isAssigned
            ? "Soutěžící byl přiřazen do disciplíny."
            : "Soutěžící byl z disciplíny odebrán.";

        return RedirectToAnchor(id, disciplineId, $"participant-{entryId}");
    }

    public async Task<IActionResult> OnPostAddMemberAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        if (!await LoadAsync(id, disciplineId, ct))
        {
            return NotFound();
        }

        var currentSlot = Math.Clamp(NewTeamVisibleSlots, 1, Setup.Discipline.TeamSize) - 1;
        if (NewTeamEntryIds[currentSlot] <= 0)
        {
            ModelState.AddModelError(string.Empty, "Nejprve vyberte člena týmu.");
            FocusTeamMemberSlot = currentSlot;
            return Page();
        }

        NewTeamVisibleSlots = Math.Min(Setup.Discipline.TeamSize, NewTeamVisibleSlots + 1);
        FocusTeamMemberSlot = NewTeamVisibleSlots - 1;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateTeamAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.CreateTeamAsync(id, disciplineId, NewTeamEntryIds, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, disciplineId, ct);
        }

        TeamStatusMessage = "Tým byl vytvořen.";
        return RedirectToAnchor(id, disciplineId, "team-list");
    }

    public async Task<IActionResult> OnPostRandomizeTeamsAsync(long id, long disciplineId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.RandomizeTeamsAsync(id, disciplineId, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, disciplineId, ct);
        }

        TeamStatusMessage = "Týmy byly rozlosovány.";
        return RedirectToAnchor(id, disciplineId, "team-feedback");
    }

    public async Task<IActionResult> OnPostDeleteTeamAsync(long id, long disciplineId, long teamId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await disciplines.DeleteTeamAsync(id, disciplineId, teamId, ct))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(id, disciplineId, ct);
        }

        TeamStatusMessage = "Tým byl smazán.";
        return RedirectToAnchor(id, disciplineId, "team-feedback");
    }

    public IReadOnlyList<DisciplineParticipant> GetOptionsForSlot(int slotIndex)
    {
        var selected = NewTeamEntryIds
            .Where((entryId, index) => index != slotIndex && entryId > 0)
            .ToHashSet();

        return Setup.AvailableForTeams
            .Where(x => !selected.Contains(x.EntryId) ||
                (slotIndex < NewTeamEntryIds.Count && NewTeamEntryIds[slotIndex] == x.EntryId))
            .OrderBy(x => x.Seed)
            .ToList();
    }

    private async Task<IActionResult> ReloadAsync(long id, long disciplineId, CancellationToken ct) =>
        await LoadAsync(id, disciplineId, ct) ? Page() : NotFound();

    private async Task<bool> LoadAsync(long id, long disciplineId, CancellationToken ct)
    {
        var setup = await disciplines.GetParticipantSetupAsync(id, disciplineId, ct);
        if (setup is null)
        {
            return false;
        }

        Setup = setup;
        NewTeamEntryIds = NormalizeNewTeamEntries(NewTeamEntryIds, setup.Discipline.TeamSize);
        NewTeamVisibleSlots = Math.Clamp(NewTeamVisibleSlots, 1, Math.Max(1, setup.Discipline.TeamSize));
        return true;
    }

    private IActionResult RedirectToAnchor(long id, long disciplineId, string anchor)
    {
        var path = Url.Page("/Editions/DisciplineParticipants", new { id, disciplineId });
        return LocalRedirect($"{path}#{anchor}");
    }

    private static List<long> NormalizeNewTeamEntries(List<long> values, int teamSize)
    {
        var normalized = values.Take(teamSize).ToList();
        while (normalized.Count < teamSize)
        {
            normalized.Add(0);
        }

        return normalized;
    }
}
