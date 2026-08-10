using System.ComponentModel.DataAnnotations;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed record PhaseMatchListViewModel(
    IReadOnlyList<PhaseSetupMatch> Matches,
    bool UsesSetScores,
    int? SetsToWin,
    bool CanEdit,
    bool ShowMatchName,
    string? StatusMessage = null,
    long? StatusMatchId = null,
    bool CanEditTeams = false,
    IReadOnlyList<PhaseSetupTeam>? TeamOptions = null,
    IReadOnlyList<PhaseSetupMatchSource>? SourceOptions = null,
    string? ValidationMessage = null,
    long? ValidationMatchId = null);

public sealed class DisciplinePhasesModel(IPhaseSetupService phases) : PageModel
{
    public DisciplinePhaseSetup Setup { get; private set; } = null!;

    [BindProperty]
    public PhaseInput NewPhase { get; set; } = new();

    [BindProperty]
    public PhaseGroupInput NewGroup { get; set; } = new();

    [BindProperty]
    public MatchSlotInput NewMatch { get; set; } = new();

    [BindProperty]
    public RandomGroupAssignmentInput RandomAssignment { get; set; } = new();

    [BindProperty]
    public MatchResultInput ResultInput { get; set; } = new();

    [BindProperty]
    public MatchTeamsInput MatchTeamsInput { get; set; } = new();

    [BindProperty]
    public MatchSetScoresInput SetScoresInput { get; set; } = new();

    [BindProperty]
    public PlayingSystemType SelectedPlayingSystem { get; set; }

    public string? ValidationSection { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? MatchStatusMessage { get; set; }

    [TempData]
    public string? MatchStatusMatchKey { get; set; }

    public long? MatchStatusMatchId => long.TryParse(MatchStatusMatchKey, out var matchId) ? matchId : null;

    public string? MatchValidationMessage { get; private set; }

    public long? MatchValidationMatchId { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, long disciplineId, CancellationToken ct) =>
        await LoadAsync(id, disciplineId, ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostCreatePhaseAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.CreatePhaseAsync(id, disciplineId, NewPhase, ct), "Fáze byla vytvořena.");

    public async Task<IActionResult> OnPostCreateGroupAsync(long id, long disciplineId, long phaseId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.CreateGroupAsync(id, disciplineId, phaseId, NewGroup, ct), "Skupina byla vytvořena.",
            validationSection: "group-create");

    public async Task<IActionResult> OnPostChangePlayingSystemAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.SetPlayingSystemAsync(id, disciplineId, SelectedPlayingSystem, ct),
            "Herní systém byl změněn.");

    public async Task<IActionResult> OnPostAssignGroupTeamsAsync(
        long id, long disciplineId, long phaseId, long groupId,
        List<long> teamIds, long focusTeamId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.AssignGroupTeamsAsync(id, disciplineId, phaseId, groupId, teamIds, ct),
            "Přiřazení týmu bylo uloženo.",
            focusTeamId > 0 ? $"group-{groupId}-team-{focusTeamId}" : $"group-{groupId}");

    public async Task<IActionResult> OnPostDeleteGroupAsync(long id, long disciplineId, long phaseId, long groupId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.DeleteGroupAsync(id, disciplineId, phaseId, groupId, ct),
            "Skupina nebo etapa byla smazána.");

    public async Task<IActionResult> OnPostDeletePhaseAsync(long id, long disciplineId, long phaseId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.DeletePhaseAsync(id, disciplineId, phaseId, ct),
            "Fáze byla smazána.");

    public async Task<IActionResult> OnPostRandomAssignAsync(long id, long disciplineId, long phaseId, long groupId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.RandomlyAssignGroupTeamsAsync(id, disciplineId, phaseId, groupId, RandomAssignment.TeamCount, ct),
            "Týmy byly náhodně přiřazeny.");

    public async Task<IActionResult> OnPostGenerateRoundRobinAsync(long id, long disciplineId, long phaseId, long groupId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            async () => await phases.GenerateRoundRobinAsync(id, disciplineId, phaseId, groupId, ct), "Rozpis zápasů byl vytvořen.");

    public async Task<IActionResult> OnPostCreateMatchAsync(long id, long disciplineId, long phaseId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.CreateMatchSlotAsync(id, disciplineId, phaseId, NewMatch, ct), "Zápasový slot byl vytvořen.");

    public async Task<IActionResult> OnPostGeneratePresetMatchesAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.GeneratePresetMatchesAsync(id, disciplineId, ct), "Všechny zápasy byly vytvořeny.");

    public async Task<IActionResult> OnPostGenerateKnockoutMatchesAsync(long id, long disciplineId, bool randomizeTeams, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.GenerateKnockoutMatchesAsync(id, disciplineId, randomizeTeams, ct),
            randomizeTeams ? "Vyřazovací pavouk byl náhodně sestaven." : "Vyřazovací pavouk je připraven k ručnímu sestavení.",
            validationSection: "knockout-generation");

    public async Task<IActionResult> OnPostUpdateKnockoutMatchTeamsAsync(long id, long disciplineId, string? focusControlId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.UpdateKnockoutMatchTeamsAsync(id, disciplineId, MatchTeamsInput, ct),
            "Obsazení zápasu bylo uloženo.",
            !string.IsNullOrWhiteSpace(focusControlId) ? focusControlId : $"match-{MatchTeamsInput.MatchId}");

    public async Task<IActionResult> OnPostResetScheduleAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.ResetScheduleAsync(id, disciplineId, ct),
            "Zápasy a přiřazení týmů byly smazány.");

    public async Task<IActionResult> OnPostLockScheduleAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.SetScheduleLockAsync(id, disciplineId, true, ct),
            "Rozpis byl uzamčen.");

    public async Task<IActionResult> OnPostUnlockScheduleAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.SetScheduleLockAsync(id, disciplineId, false, ct),
            "Rozpis byl odemčen.");

    public async Task<IActionResult> OnPostLockResultsAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.SetResultsLockAsync(id, disciplineId, true, ct),
            "Výsledky byly uzamčeny.");

    public async Task<IActionResult> OnPostUnlockResultsAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.SetResultsLockAsync(id, disciplineId, false, ct),
            "Výsledky byly odemčeny.");

    public async Task<IActionResult> OnPostDeleteResultsAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteAsync(id, disciplineId, ct,
            () => phases.DeleteResultsAsync(id, disciplineId, ct),
            "Všechny výsledky a výsledky setů byly smazány.");

    public async Task<IActionResult> OnPostUpdateResultAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteMatchEditAsync(id, disciplineId, ResultInput.MatchId, ct,
            () => phases.UpdateMatchResultAsync(id, disciplineId, ResultInput, User.Identity?.IsAuthenticated == true, ct),
            "Výsledek byl uložen.");

    public async Task<IActionResult> OnPostUpdateSetScoresAsync(long id, long disciplineId, CancellationToken ct) =>
        await ExecuteMatchEditAsync(id, disciplineId, SetScoresInput.MatchId, ct,
            () => phases.UpdateMatchSetScoresAsync(id, disciplineId, SetScoresInput, User.Identity?.IsAuthenticated == true, ct),
            "Dílčí skóre bylo uloženo.");

    public async Task<IActionResult> OnPostDeleteSetScoresAsync(long id, long disciplineId, CancellationToken ct)
    {
        SetScoresInput.Sets.Clear();
        return await ExecuteMatchEditAsync(id, disciplineId, SetScoresInput.MatchId, ct,
            () => phases.UpdateMatchSetScoresAsync(id, disciplineId, SetScoresInput, User.Identity?.IsAuthenticated == true, ct),
            "Dílčí skóre bylo smazáno.");
    }

    public IReadOnlyList<PhaseSetupTeam> GetAssignableTeams(PhaseSetupGroup group, int? directTeamCapacity = null)
    {
        if (directTeamCapacity is not null && group.TeamIds.Count >= directTeamCapacity)
        {
            return Setup.Teams.Where(x => group.TeamIds.Contains(x.Id)).ToList();
        }
        return Setup.Teams.Where(x => group.TeamIds.Contains(x.Id) || !Setup.AssignedTeamIds.Contains(x.Id)).ToList();
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

    public bool HasMatches => Setup.Phases.SelectMany(x => x.Groups.SelectMany(g => g.Matches).Concat(x.Matches)).Any();

    public bool CanEditResults(bool isAdmin) => (isAdmin || Setup.IsAnonymousResultEditingEnabled) && !Setup.AreResultsLocked &&
        (Setup.PlayingSystem == PlayingSystemType.Knockout
            ? Setup.IsScheduleLocked
            : true);

    public static string GetPhaseTypeLabel(PhaseType type) => type switch
    {
        PhaseType.Group => "Skupinová",
        PhaseType.Knockout => "Vyřazovací",
        PhaseType.FinalStanding => "O konečné umístění",
        _ => type.ToString()
    };

    private async Task<IActionResult> ExecuteAsync<T>(long editionId, long disciplineId, CancellationToken ct, Func<Task<T>> action, string message, string? fragment = null, string? validationSection = null)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            await action();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ValidationSection = validationSection;
            return await LoadAsync(editionId, disciplineId, ct) ? Page() : NotFound();
        }

        StatusMessage = message;
        return fragment is null
            ? RedirectToPage(new { id = editionId, disciplineId })
            : RedirectToPage(pageName: null, pageHandler: null,
                routeValues: new { id = editionId, disciplineId }, fragment: fragment);
    }

    private async Task<IActionResult> ExecuteMatchEditAsync<T>(long editionId, long disciplineId, long matchId, CancellationToken ct, Func<Task<T>> action, string message)
    {
        try
        {
            await action();
        }
        catch (ValidationException ex)
        {
            MatchValidationMessage = ex.Message;
            MatchValidationMatchId = matchId;
            return await LoadAsync(editionId, disciplineId, ct) ? Page() : NotFound();
        }

        MatchStatusMessage = message;
        MatchStatusMatchKey = matchId.ToString();
        return RedirectToPage(new { id = editionId, disciplineId });
    }

    private async Task<bool> LoadAsync(long editionId, long disciplineId, CancellationToken ct)
    {
        var setup = await phases.GetSetupAsync(editionId, disciplineId, ct);
        if (setup is null)
        {
            return false;
        }

        Setup = setup;
        SelectedPlayingSystem = setup.PlayingSystem;
        if (NewPhase.Order <= 0)
        {
            NewPhase.Order = setup.Phases.Count == 0 ? 1 : setup.Phases.Max(x => x.Order) + 1;
        }
        return true;
    }
}
