using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Competition.Configuration;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Competition.Pages.Editions;
using Competition.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Competition.Tests;

public sealed class PhaseSetupServiceTests
{
    [Fact]
    public async Task RoundRobinPreset_IsFixedAndAssignsEveryTeamAutomatically()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 5, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);

        var setup = await service.GetSetupAsync(editionId, disciplineId);

        var phase = Assert.Single(setup!.Phases);
        Assert.Equal("Detaily", phase.Name);
        Assert.Equal((2, 1, 0), (phase.PointsForWin, phase.PointsForDraw, phase.PointsForLoss));
        Assert.Equal(teamIds, Assert.Single(phase.Groups).TeamIds);

        Assert.Equal(10, await service.GeneratePresetMatchesAsync(editionId, disciplineId));
        Assert.Equal(5, (await db.Matches.ToListAsync()).Select(x => x.Name.Split('–')[1].Split('.')[0].Trim()).Distinct().Count());
    }

    [Fact]
    public async Task GenerateRandomResults_CompletesEveryMatchWithConsistentSetScores()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 4, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);

        Assert.Equal(6, await service.GenerateRandomResultsAsync(editionId, disciplineId));

        var matches = await db.Matches.Include(x => x.SetScores).ToListAsync();
        Assert.All(matches, match =>
        {
            Assert.Equal(MatchStatus.Completed, match.Status);
            Assert.True(match.HomeScore == 2 || match.AwayScore == 2);
            Assert.NotEqual(match.HomeScore, match.AwayScore);
            Assert.Equal(match.HomeScore + match.AwayScore, match.SetScores.Count);
            Assert.Equal(match.HomeScore, match.SetScores.Count(x => x.HomeScore > x.AwayScore));
            Assert.Equal(match.AwayScore, match.SetScores.Count(x => x.AwayScore > x.HomeScore));
        });
    }

    [Fact]
    public async Task TwoGroupPreset_GeneratesBergerFixturesAndClassificationSlots()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 6, PlayingSystemType.GroupsThenClassificationMatches);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var groupPhases = setup!.Phases.Where(x => x.Type == PhaseType.Group).OrderBy(x => x.Order).ToList();

        await service.AssignGroupTeamsAsync(editionId, disciplineId, groupPhases[0].Id, groupPhases[0].Groups[0].Id, teamIds.Take(3).ToList());
        await service.AssignGroupTeamsAsync(editionId, disciplineId, groupPhases[1].Id, groupPhases[1].Groups[0].Id, teamIds.Skip(3).ToList());

        Assert.Equal(8, await service.GeneratePresetMatchesAsync(editionId, disciplineId));
        Assert.Equal(6, await db.Matches.CountAsync(x => x.PhaseGroupId != null));
        var classification = await db.Matches.Where(x => x.PhaseGroupId == null).OrderBy(x => x.Order).ToListAsync();
        Assert.Equal(new[] { "Finále", "O 3. místo" }, classification.Select(x => x.Name));
        Assert.Equal((1, 1), (classification[0].HomeSourceRank, classification[0].AwaySourceRank));
        Assert.Equal((2, 2), (classification[1].HomeSourceRank, classification[1].AwaySourceRank));
    }

    [Fact]
    public async Task TwoGroupClassification_FillsFinalStandingTeamsAfterBothGroupsAreComplete()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 4, PlayingSystemType.GroupsThenClassificationMatches);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var groupPhases = setup!.Phases.Where(x => x.Type == PhaseType.Group).OrderBy(x => x.Order).ToList();

        await service.AssignGroupTeamsAsync(editionId, disciplineId, groupPhases[0].Id, groupPhases[0].Groups[0].Id, teamIds.Take(2).ToList());
        await service.AssignGroupTeamsAsync(editionId, disciplineId, groupPhases[1].Id, groupPhases[1].Groups[0].Id, teamIds.Skip(2).ToList());
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);

        setup = await service.GetSetupAsync(editionId, disciplineId);
        var finalMatches = setup!.Phases.Single(x => x.Type == PhaseType.FinalStanding).Matches.OrderBy(x => x.Order).ToList();
        Assert.All(finalMatches, match =>
        {
            Assert.Null(match.HomeTeamId);
            Assert.Null(match.AwayTeamId);
        });

        var groupMatches = db.Matches.Where(x => x.PhaseGroupId != null).OrderBy(x => x.PhaseGroupId).ToList();
        await service.UpdateMatchResultAsync(editionId, disciplineId, new MatchResultInput
        {
            MatchId = groupMatches[0].Id,
            HomeScore = 2,
            AwayScore = 0,
            Version = groupMatches[0].Version
        }, false);

        setup = await service.GetSetupAsync(editionId, disciplineId);
        finalMatches = setup!.Phases.Single(x => x.Type == PhaseType.FinalStanding).Matches.OrderBy(x => x.Order).ToList();
        Assert.Null(finalMatches[0].AwayTeamId);
        Assert.Null(finalMatches[1].AwayTeamId);

        groupMatches = db.Matches.Where(x => x.PhaseGroupId != null).OrderBy(x => x.PhaseGroupId).ToList();
        await service.UpdateMatchResultAsync(editionId, disciplineId, new MatchResultInput
        {
            MatchId = groupMatches[1].Id,
            HomeScore = 0,
            AwayScore = 2,
            Version = groupMatches[1].Version
        }, false);

        setup = await service.GetSetupAsync(editionId, disciplineId);
        finalMatches = setup!.Phases.Single(x => x.Type == PhaseType.FinalStanding).Matches.OrderBy(x => x.Order).ToList();
        Assert.Equal((teamIds[0], teamIds[3]), (finalMatches[0].HomeTeamId, finalMatches[0].AwayTeamId));
        Assert.Equal((teamIds[1], teamIds[2]), (finalMatches[1].HomeTeamId, finalMatches[1].AwayTeamId));
        Assert.Equal("1. místo – Skupina A", finalMatches[0].HomeSource);
        Assert.Equal("1. místo – Skupina B", finalMatches[0].AwaySource);
        Assert.Equal("2. místo – Skupina A", finalMatches[1].HomeSource);
        Assert.Equal("2. místo – Skupina B", finalMatches[1].AwaySource);
    }

    [Fact]
    public async Task KnockoutStages_FeedWinnersIntoNextStage()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 4, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var phase = Assert.Single(setup!.Phases);
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 1, Capacity = 4 });
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 2, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, semifinalId, teamIds);

        Assert.Equal(3, await service.GeneratePresetMatchesAsync(editionId, disciplineId));
        var matches = await db.Matches.OrderBy(x => x.Order).ToListAsync();
        Assert.Equal(2, matches.Count(x => x.PhaseGroupId == semifinalId));
        var final = matches[^1];
        Assert.Equal(new[] { "Semifinále 1", "Semifinále 2", "Finále" }, matches.Select(x => x.Name));
        Assert.NotNull(final.HomeSourceMatchId);
        Assert.NotNull(final.AwaySourceMatchId);
        Assert.Contains(final.HomeSourceMatchId.Value, matches.Take(2).Select(x => x.Id));
        Assert.Contains(final.AwaySourceMatchId.Value, matches.Take(2).Select(x => x.Id));

        var firstSemifinal = matches[0];
        var advancingTeamId = firstSemifinal.HomeTeamId;
        await service.SetScheduleLockAsync(editionId, disciplineId, true);
        await service.UpdateMatchResultAsync(editionId, disciplineId, new MatchResultInput
        {
            MatchId = firstSemifinal.Id,
            HomeScore = 2,
            AwayScore = 0,
            Version = firstSemifinal.Version
        }, false);

        var refreshedSetup = await service.GetSetupAsync(editionId, disciplineId);
        var refreshedFinal = refreshedSetup!.Phases.Single().Groups.Single(x => x.Name == "Finále").Matches.Single();
        Assert.Equal(advancingTeamId, refreshedFinal.HomeTeamId);
        Assert.Equal("Vítěz – Semifinále 1", refreshedFinal.HomeSource);
        Assert.Equal("Semifinále 1", refreshedFinal.HomeAdvancementSource);
        Assert.Equal("Vítěz – Semifinále 2", refreshedFinal.AwaySource);
    }

    [Fact]
    public async Task KnockoutResult_MustHaveWinner()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 2, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var phase = Assert.Single(setup!.Phases);
        var finalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 1, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, finalId, teamIds);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchResultAsync(
            editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = 1, AwayScore = 1, Version = match.Version },
            false));
    }

    [Fact]
    public async Task KnockoutSetup_ReconcilesWinnerSavedBeforeAdvancementSupport()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 4, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var phase = Assert.Single(setup!.Phases);
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 1, Capacity = 4 });
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 2, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, semifinalId, teamIds);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var semifinal = await db.Matches.OrderBy(x => x.Order).FirstAsync();
        semifinal.HomeScore = 2;
        semifinal.AwayScore = 0;
        semifinal.Status = MatchStatus.Completed;
        await db.SaveChangesAsync();

        var refreshedSetup = await service.GetSetupAsync(editionId, disciplineId);
        var final = refreshedSetup!.Phases.Single().Groups.Single(x => x.Name == "Finále").Matches.Single();

        Assert.Equal(semifinal.HomeTeamId, final.HomeTeamId);
        Assert.Equal("Semifinále 1", final.HomeAdvancementSource);
    }

    [Fact]
    public async Task FirstResultLocksScheduleAndProtectsResultsFromReset()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = 2, AwayScore = 1, Version = match.Version }, false);
        Assert.True((await service.GetSetupAsync(editionId, disciplineId))!.IsScheduleLocked);
        await service.SetScheduleLockAsync(editionId, disciplineId, true);

        var lockedSetup = await service.GetSetupAsync(editionId, disciplineId);
        Assert.True(lockedSetup!.IsScheduleLocked);

        await Assert.ThrowsAsync<ValidationException>(() => service.ResetScheduleAsync(editionId, disciplineId));

        await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = 3, AwayScore = 1, Version = match.Version }, false);
        Assert.Equal(3, (await db.Matches.SingleAsync()).HomeScore);
    }

    [Fact]
    public async Task ScheduleLockPageHandlers_SetExplicitRequestedState()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin")], "test"))
                }
            }
        };

        var lockRedirect = Assert.IsType<RedirectToPageResult>(
            await page.OnPostLockScheduleAsync(editionId, disciplineId, default));
        Assert.Equal("/Editions/DisciplineResults", lockRedirect.PageName);
        Assert.True((await service.GetSetupAsync(editionId, disciplineId))!.IsScheduleLocked);

        var unlockRedirect = Assert.IsType<RedirectToPageResult>(
            await page.OnPostUnlockScheduleAsync(editionId, disciplineId, default));
        Assert.Equal("/Editions/DisciplinePhases", unlockRedirect.PageName);
        Assert.False((await service.GetSetupAsync(editionId, disciplineId))!.IsScheduleLocked);
    }

    [Fact]
    public async Task ScheduleAndResultsPages_RouteAccordingToScheduleLock()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);

        var schedulePage = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        var resultsPage = new DisciplineResultsModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<PageResult>(await schedulePage.OnGetAsync(editionId, disciplineId, default));
        Assert.False(schedulePage.CanEditResults(true));
        var unlockedResultsRedirect = Assert.IsType<RedirectToPageResult>(
            await resultsPage.OnGetAsync(editionId, disciplineId, default));
        Assert.Equal("/Editions/DisciplinePhases", unlockedResultsRedirect.PageName);

        await service.SetScheduleLockAsync(editionId, disciplineId, true);

        var lockedScheduleRedirect = Assert.IsType<RedirectToPageResult>(
            await schedulePage.OnGetAsync(editionId, disciplineId, default));
        Assert.Equal("/Editions/DisciplineResults", lockedScheduleRedirect.PageName);
        Assert.IsType<PageResult>(await resultsPage.OnGetAsync(editionId, disciplineId, default));
        Assert.True(resultsPage.CanEditResults(true));
    }

    [Fact]
    public async Task ResultValidation_IsAttachedToMatchRow()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            ResultInput = new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = 3,
                AwayScore = 1,
                Version = match.Version
            }
        };

        var result = await page.OnPostUpdateResultAsync(editionId, disciplineId, default);

        Assert.IsType<PageResult>(result);
        Assert.Empty(page.ModelState);
        Assert.Equal(match.Id, page.MatchValidationMatchId);
        Assert.Contains("Hlavní výsledek", page.MatchValidationMessage);
    }

    [Fact]
    public async Task DeleteResults_ClearsKnockoutFromLastStageAndRemovesAdvancingTeams()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 4, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var phase = Assert.Single((await service.GetSetupAsync(editionId, disciplineId))!.Phases);
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 1, Capacity = 4 });
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 2, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, semifinalId, teamIds);
        await service.GenerateKnockoutMatchesAsync(editionId, disciplineId, true);
        await service.SetScheduleLockAsync(editionId, disciplineId, true);

        var semifinals = await db.Matches.OrderBy(x => x.Order).Take(2).ToListAsync();
        foreach (var semifinal in semifinals)
        {
            await service.UpdateMatchResultAsync(editionId, disciplineId,
                new MatchResultInput { MatchId = semifinal.Id, HomeScore = 2, AwayScore = 0, Version = semifinal.Version }, true);
        }
        var final = await db.Matches.OrderBy(x => x.Order).LastAsync();
        await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = final.Id, HomeScore = 2, AwayScore = 1, Version = final.Version }, true);

        await service.DeleteResultsAsync(editionId, disciplineId);

        var cleared = await db.Matches.OrderBy(x => x.Order).ToListAsync();
        Assert.All(cleared, x =>
        {
            Assert.Null(x.HomeScore);
            Assert.Null(x.AwayScore);
            Assert.Equal(MatchStatus.Scheduled, x.Status);
        });
        Assert.Null(cleared[^1].HomeTeamId);
        Assert.Null(cleared[^1].AwayTeamId);
    }

    [Fact]
    public async Task ManualKnockoutMatchups_CanBeChangedOnlyBeforeScheduleLock()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 4, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var phase = Assert.Single((await service.GetSetupAsync(editionId, disciplineId))!.Phases);
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 1, Capacity = 4 });
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 2, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, semifinalId, teamIds);
        await service.GenerateKnockoutMatchesAsync(editionId, disciplineId, false);
        var blankSemifinals = await db.Matches.OrderBy(x => x.Order).Take(2).ToListAsync();
        Assert.All(blankSemifinals, x =>
        {
            Assert.Null(x.HomeTeamId);
            Assert.Null(x.AwayTeamId);
        });
        var match = await db.Matches.OrderBy(x => x.Order).FirstAsync();

        await service.UpdateKnockoutMatchTeamsAsync(editionId, disciplineId, new MatchTeamsInput
        {
            MatchId = match.Id,
            HomeTeamId = teamIds[1],
            AwayTeamId = teamIds[0]
        });
        var swapped = await db.Matches.SingleAsync(x => x.Id == match.Id);
        Assert.Equal(teamIds[1], swapped.HomeTeamId);
        Assert.Equal(teamIds[0], swapped.AwayTeamId);

        var secondSemifinal = await db.Matches.OrderBy(x => x.Order).Skip(1).FirstAsync();
        await service.UpdateKnockoutMatchTeamsAsync(editionId, disciplineId, new MatchTeamsInput
        {
            MatchId = secondSemifinal.Id,
            HomeTeamId = teamIds[2],
            AwayTeamId = teamIds[3]
        });
        var final = await db.Matches.OrderBy(x => x.Order).LastAsync();
        Assert.Null(final.HomeSourceMatchId);
        Assert.Null(final.AwaySourceMatchId);
        await service.UpdateKnockoutMatchTeamsAsync(editionId, disciplineId, new MatchTeamsInput
        {
            MatchId = final.Id,
            HomeSelection = $"match:{match.Id}",
            AwaySelection = $"match:{secondSemifinal.Id}"
        });
        var linkedFinal = await db.Matches.SingleAsync(x => x.Id == final.Id);
        Assert.Equal(match.Id, linkedFinal.HomeSourceMatchId);
        Assert.Equal(secondSemifinal.Id, linkedFinal.AwaySourceMatchId);

        await service.SetScheduleLockAsync(editionId, disciplineId, true);
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateKnockoutMatchTeamsAsync(
            editionId, disciplineId,
            new MatchTeamsInput { MatchId = match.Id, HomeTeamId = teamIds[0], AwayTeamId = teamIds[1] }));
    }

    [Fact]
    public async Task PlayingSystem_CanChangeBeforeMatchesButNotAfterGeneration()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 4, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);

        Assert.True(await service.SetPlayingSystemAsync(editionId, disciplineId, PlayingSystemType.Knockout));
        var knockoutSetup = await service.GetSetupAsync(editionId, disciplineId);
        Assert.Equal(PlayingSystemType.Knockout, knockoutSetup!.PlayingSystem);
        Assert.Equal(PhaseType.Knockout, Assert.Single(knockoutSetup.Phases).Type);

        var phase = Assert.Single(knockoutSetup.Phases);
        var finalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 1, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, finalId,
            knockoutSetup.Teams.Take(2).Select(x => x.Id).ToList());
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        await Assert.ThrowsAsync<ValidationException>(() => service.SetPlayingSystemAsync(
            editionId, disciplineId, PlayingSystemType.RoundRobin));
    }

    [Fact]
    public async Task KnockoutStage_AllowsOnlyDirectTeamsNotFilledByPreviousWinners()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 6, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var phase = Assert.Single((await service.GetSetupAsync(editionId, disciplineId))!.Phases);
        var preliminaryId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Předkolo", Order = 1, Capacity = 2 });
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 2, Capacity = 4 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, preliminaryId, teamIds.Take(2).ToList());

        await Assert.ThrowsAsync<ValidationException>(() => service.AssignGroupTeamsAsync(
            editionId, disciplineId, phase.Id, semifinalId, teamIds.Skip(2).Take(4).ToList()));
        Assert.True(await service.AssignGroupTeamsAsync(
            editionId, disciplineId, phase.Id, semifinalId, teamIds.Skip(2).Take(3).ToList()));
    }

    [Fact]
    public async Task KnockoutStage_RejectsCapacityLowerThanPreviousAdvancingTeams()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 10, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var phase = Assert.Single((await service.GetSetupAsync(editionId, disciplineId))!.Phases);
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Předkolo", Order = 1, Capacity = 4 });
        await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Čtvrtfinále", Order = 2, Capacity = 8 });

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 3, Capacity = 2 }));
    }

    [Fact]
    public async Task GenerateKnockoutMatches_AllowsTenTeamProgressionWithFinalFedOnlyBySemifinals()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 10, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var phase = Assert.Single((await service.GetSetupAsync(editionId, disciplineId))!.Phases);
        var preliminaryId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Předkolo", Order = 1, Capacity = 4 });
        var quarterfinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Čtvrtfinále", Order = 2, Capacity = 8 });
        var semifinalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Semifinále", Order = 3, Capacity = 4 });
        var finalId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 4, Capacity = 2 });

        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, preliminaryId, teamIds.Take(4).ToList());
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, quarterfinalId, teamIds.Skip(4).Take(6).ToList());
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, semifinalId, []);
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, finalId, []);

        Assert.Equal(9, await service.GenerateKnockoutMatchesAsync(editionId, disciplineId, true));

        var final = await db.Matches.SingleAsync(x => x.PhaseGroupId == finalId);
        Assert.Null(final.HomeTeamId);
        Assert.Null(final.AwayTeamId);
        Assert.NotNull(final.HomeSourceMatchId);
        Assert.NotNull(final.AwaySourceMatchId);
    }

    [Fact]
    public async Task ResetSchedule_WithoutResults_RemovesMatchesAndAssignmentsButKeepsPhases()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);

        await service.ResetScheduleAsync(editionId, disciplineId);

        Assert.Equal(setup!.Phases.Count, await db.DisciplinePhases.CountAsync());
        Assert.Empty(await db.Matches.ToListAsync());
        Assert.Empty(await db.PhaseGroupTeams.ToListAsync());
    }

    [Fact]
    public async Task ResetSchedule_IsRejectedWhileScheduleIsLocked()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        await service.SetScheduleLockAsync(editionId, disciplineId, true);

        await Assert.ThrowsAsync<ValidationException>(() => service.ResetScheduleAsync(editionId, disciplineId));
        Assert.Single(await db.Matches.ToListAsync());
    }

    [Fact]
    public async Task ResetSchedule_UnlockedWithResults_IsRejected()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = 1, AwayScore = 0, Version = match.Version }, false);

        await Assert.ThrowsAsync<ValidationException>(() => service.ResetScheduleAsync(editionId, disciplineId));

        Assert.Single(await db.Matches.ToListAsync());
        Assert.NotEmpty(await db.PhaseGroupTeams.ToListAsync());
    }

    [Fact]
    public async Task SetScores_AreNarrowlyUpdatedAndMarkMatchInProgress()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        (await db.CompetitionDisciplines.SingleAsync()).UsesSetScores = true;
        (await db.CompetitionDisciplines.SingleAsync()).SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await service.UpdateMatchSetScoresAsync(editionId, disciplineId, new MatchSetScoresInput
        {
            MatchId = match.Id,
            Version = match.Version,
            Sets =
            [
                new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 4 },
                new MatchSetScoreInput { SetNumber = 2 },
                new MatchSetScoreInput { SetNumber = 3 }
            ]
        }, false);

        var saved = await db.Matches.Include(x => x.SetScores).SingleAsync();
        var set = Assert.Single(saved.SetScores);
        Assert.Equal((1, 6, 4), (set.SetNumber, set.HomeScore, set.AwayScore));
        Assert.Null(saved.HomeScore);
        Assert.Null(saved.AwayScore);
        Assert.Equal(MatchStatus.InProgress, saved.Status);
    }

    [Fact]
    public async Task SetScores_CalculateAndSaveMissingMainScoreWhenMatchIsDecided()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        Assert.True(await service.UpdateMatchSetScoresAsync(editionId, disciplineId,
            new MatchSetScoresInput
            {
                MatchId = match.Id,
                Version = match.Version,
                Sets =
                [
                    new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 3 },
                    new MatchSetScoreInput { SetNumber = 2, HomeScore = 4, AwayScore = 6 },
                    new MatchSetScoreInput { SetNumber = 3, HomeScore = 6, AwayScore = 2 }
                ]
            }, false));

        var saved = await db.Matches.SingleAsync();
        Assert.Equal((2, 1), (saved.HomeScore, saved.AwayScore));
        Assert.Equal(MatchStatus.Completed, saved.Status);
    }

    [Fact]
    public async Task SetScores_RejectSetsPlayedAfterBestOfThreeWasAlreadyWon()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchSetScoresAsync(
            editionId, disciplineId,
            new MatchSetScoresInput
            {
                MatchId = match.Id,
                Version = match.Version,
                Sets =
                [
                    new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 3 },
                    new MatchSetScoreInput { SetNumber = 2, HomeScore = 6, AwayScore = 4 },
                    new MatchSetScoreInput { SetNumber = 3, HomeScore = 6, AwayScore = 2 }
                ]
            }, false));

        Assert.Contains("další sety", error.Message);
        Assert.Null((await db.Matches.SingleAsync()).HomeScore);
        Assert.Empty(await db.MatchSetScores.ToListAsync());
    }

    [Fact]
    public async Task InvalidSetScoresHandler_ReopensModalAndPreservesSubmittedScores()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        (await db.CompetitionDisciplines.SingleAsync()).UsesSetScores = true;
        (await db.CompetitionDisciplines.SingleAsync()).SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var submitted = new MatchSetScoresInput
        {
            MatchId = match.Id,
            Version = match.Version,
            Sets =
            [
                new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = null },
                new MatchSetScoreInput { SetNumber = 2, HomeScore = 3, AwayScore = 6 }
            ]
        };
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            SetScoresInput = submitted
        };

        Assert.IsType<PageResult>(await page.OnPostUpdateSetScoresAsync(editionId, disciplineId, default));

        Assert.True(page.MatchValidationIsSetScores);
        Assert.Equal(match.Id, page.MatchValidationMatchId);
        Assert.Contains("obě hodnoty", page.MatchValidationMessage);
        Assert.Equal((6, null),
            (page.SetScoresInput.Sets[0].HomeScore, page.SetScoresInput.Sets[0].AwayScore));
        Assert.Equal((3, 6),
            (page.SetScoresInput.Sets[1].HomeScore, page.SetScoresInput.Sets[1].AwayScore));
    }

    [Fact]
    public async Task DeleteSetScoresHandler_RemovesSetsAndTargetsMatchNotification()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        (await db.CompetitionDisciplines.SingleAsync()).UsesSetScores = true;
        (await db.CompetitionDisciplines.SingleAsync()).SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        await service.UpdateMatchSetScoresAsync(editionId, disciplineId, new MatchSetScoresInput
        {
            MatchId = match.Id,
            Version = match.Version,
            Sets = [new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 3 }]
        }, false);
        var savedMatch = await db.Matches.SingleAsync();
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            SetScoresInput = new MatchSetScoresInput { MatchId = savedMatch.Id, Version = savedMatch.Version }
        };

        Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteSetScoresAsync(editionId, disciplineId, default));

        Assert.Empty(await db.MatchSetScores.ToListAsync());
        Assert.Equal(MatchStatus.Scheduled, (await db.Matches.SingleAsync()).Status);
        Assert.Equal("Dílčí skóre bylo smazáno.", page.MatchStatusMessage);
        Assert.Equal(match.Id, page.MatchStatusMatchId);
    }

    [Fact]
    public async Task AnonymousResultEditing_GlobalSwitchBlocksVisitorsButNotAdmins()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db, Options.Create(new AnonymousResultEditingSettings { Enabled = false }));
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var input = new MatchResultInput
        {
            MatchId = match.Id,
            HomeScore = 2,
            AwayScore = 1,
            Version = match.Version
        };

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateMatchResultAsync(editionId, disciplineId, input, false));

        Assert.Contains("uzavřená", error.Message);
        Assert.False((await service.GetSetupAsync(editionId, disciplineId))!.IsAnonymousResultEditingEnabled);
        Assert.True(await service.UpdateMatchResultAsync(editionId, disciplineId, input, true));
    }

    [Fact]
    public async Task StaleResultEditShowsConflictAndReloadsCurrentResult()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var staleVersion = match.Version;
        await service.UpdateMatchResultAsync(editionId, disciplineId, new MatchResultInput
        {
            MatchId = match.Id,
            HomeScore = 2,
            AwayScore = 0,
            Version = staleVersion
        }, false);
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            ResultInput = new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = 0,
                AwayScore = 2,
                Version = staleVersion
            }
        };

        Assert.IsType<PageResult>(await page.OnPostUpdateResultAsync(editionId, disciplineId, default));

        Assert.Contains("někdo jiný", page.MatchValidationMessage);
        var current = page.Setup.Phases.SelectMany(x => x.Groups.SelectMany(g => g.Matches).Concat(x.Matches))
            .Single(x => x.Id == match.Id);
        Assert.Equal((2, 0), (current.HomeScore, current.AwayScore));
    }

    [Fact]
    public async Task AnonymousResultEditCannotChangeMatchAdministrationFields()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var original = (match.DisciplinePhaseId, match.PhaseGroupId, match.HomeTeamId, match.AwayTeamId, match.Name, match.Order);

        await service.UpdateMatchResultAsync(editionId, disciplineId, new MatchResultInput
        {
            MatchId = match.Id,
            HomeScore = 4,
            AwayScore = 3,
            Version = match.Version
        }, false);

        var saved = await db.Matches.SingleAsync();
        Assert.Equal(original, (saved.DisciplinePhaseId, saved.PhaseGroupId, saved.HomeTeamId, saved.AwayTeamId, saved.Name, saved.Order));
    }

    [Fact]
    public async Task SetScoresRejectDuplicateOrSkippedSetNumbers()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchSetScoresAsync(
            editionId, disciplineId, new MatchSetScoresInput
            {
                MatchId = match.Id,
                Version = match.Version,
                Sets =
                [
                    new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 3 },
                    new MatchSetScoreInput { SetNumber = 1, HomeScore = 2, AwayScore = 6 }
                ]
            }, false));

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchSetScoresAsync(
            editionId, disciplineId, new MatchSetScoresInput
            {
                MatchId = match.Id,
                Version = match.Version,
                Sets = [new MatchSetScoreInput { SetNumber = 2, HomeScore = 6, AwayScore = 3 }]
            }, false));
    }

    [Fact]
    public async Task AnonymousUserCanEditResultsButCannotUseAdministrationHandlers()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            ResultInput = new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = 1,
                AwayScore = 0,
                Version = match.Version
            }
        };

        Assert.IsType<RedirectToPageResult>(await page.OnPostUpdateResultAsync(editionId, disciplineId, default));
    }

    [Fact]
    public async Task ResultEditRequiresVersionAndDistinctAssignedTeams()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchResultAsync(
            editionId, disciplineId, new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = 1,
                AwayScore = 0
            }, false));

        match.AwayTeamId = match.HomeTeamId;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchResultAsync(
            editionId, disciplineId, new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = 1,
                AwayScore = 0,
                Version = match.Version
            }, false));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 0, 3)]
    [InlineData(3, 2, 3)]
    public async Task SetBasedMainScore_AcceptsContinentalWinningSetVariants(int setsToWin, int homeScore, int awayScore)
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = setsToWin;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        Assert.True(await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = homeScore, AwayScore = awayScore, Version = match.Version },
            false));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 2, 1)]
    [InlineData(3, 4, 0)]
    public async Task SetBasedMainScore_RejectsScoresWithoutExactlyOneWinner(int setsToWin, int homeScore, int awayScore)
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = setsToWin;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = homeScore, AwayScore = awayScore, Version = match.Version },
            false));
    }

    [Fact]
    public async Task ThreeWinningSets_AllowsFivePlayedSetsConsistentWithThreeToTwoResult()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2, PlayingSystemType.RoundRobin);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 3;
        await db.SaveChangesAsync();
        var service = new PhaseSetupService(db);
        await service.GetSetupAsync(editionId, disciplineId);
        await service.GeneratePresetMatchesAsync(editionId, disciplineId);
        var match = await db.Matches.SingleAsync();
        await service.UpdateMatchResultAsync(editionId, disciplineId,
            new MatchResultInput { MatchId = match.Id, HomeScore = 3, AwayScore = 2, Version = match.Version }, false);
        match = await db.Matches.SingleAsync();

        Assert.True(await service.UpdateMatchSetScoresAsync(editionId, disciplineId, new MatchSetScoresInput
        {
            MatchId = match.Id,
            Version = match.Version,
            Sets =
            [
                new MatchSetScoreInput { SetNumber = 1, HomeScore = 6, AwayScore = 3 },
                new MatchSetScoreInput { SetNumber = 2, HomeScore = 4, AwayScore = 6 },
                new MatchSetScoreInput { SetNumber = 3, HomeScore = 6, AwayScore = 2 },
                new MatchSetScoreInput { SetNumber = 4, HomeScore = 5, AwayScore = 7 },
                new MatchSetScoreInput { SetNumber = 5, HomeScore = 6, AwayScore = 4 }
            ]
        }, false));
        Assert.Equal(5, await db.MatchSetScores.CountAsync());
    }

    [Theory]
    [InlineData(0, 0, 2, false)]
    [InlineData(4, 0, 2, false)]
    [InlineData(4, 1, 2, false)]
    [InlineData(4, 2, 2, true)]
    [InlineData(3, 3, 1, true)]
    public void PhaseSetupAvailability_RequiresParticipantsAndAllCompleteTeams(
        int participantCount, int teamCount, int teamSize, bool expected)
    {
        var discipline = new ConfiguredDisciplineItem(
            1, 1, "Tenis", 1, PlayingSystemType.RoundRobin,
            teamSize, null, false, teamCount, participantCount);

        Assert.Equal(expected, discipline.IsPhaseSetupAvailable);
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(5, 10)]
    public async Task GenerateRoundRobin_CreatesEveryPairExactlyOnce(int teamCount, int expectedMatches)
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, teamCount);
        var service = new PhaseSetupService(db);
        var phaseId = await service.CreatePhaseAsync(editionId, disciplineId, new PhaseInput { Name = "Skupiny", Type = PhaseType.Group, Order = 1 });
        var groupId = await service.CreateGroupAsync(editionId, disciplineId, phaseId, new PhaseGroupInput { Name = "A", Order = 1 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phaseId, groupId, teamIds);

        var count = await service.GenerateRoundRobinAsync(editionId, disciplineId, phaseId, groupId);

        var matches = await db.Matches.OrderBy(x => x.Order).ToListAsync();
        Assert.Equal(expectedMatches, count);
        Assert.Equal(expectedMatches, matches.Count);
        Assert.Equal(expectedMatches, matches.Select(x => new[] { x.HomeTeamId!.Value, x.AwayTeamId!.Value }.Order().ToArray()).Distinct(new PairComparer()).Count());
        Assert.All(teamIds, teamId => Assert.Equal(teamCount - 1, matches.Count(x => x.HomeTeamId == teamId || x.AwayTeamId == teamId)));
        Assert.All(teamIds, teamId =>
        {
            var homeMatches = matches.Count(x => x.HomeTeamId == teamId);
            var awayMatches = matches.Count(x => x.AwayTeamId == teamId);
            Assert.InRange(Math.Abs(homeMatches - awayMatches), 0, 1);
        });
    }

    [Fact]
    public async Task CreateMatchSlot_AllowsTeamsToBeAssignedLater()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2);
        var service = new PhaseSetupService(db);
        var phaseId = await service.CreatePhaseAsync(editionId, disciplineId, new PhaseInput { Name = "O umístění", Type = PhaseType.FinalStanding, Order = 1 });

        await service.CreateMatchSlotAsync(editionId, disciplineId, phaseId, new MatchSlotInput { Name = "Finále" });

        var match = await db.Matches.SingleAsync();
        Assert.Null(match.HomeTeamId);
        Assert.Null(match.AwayTeamId);
    }

    [Fact]
    public async Task AssignGroupTeams_RejectsTeamFromAnotherDiscipline()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, _) = await SeedDisciplineAsync(db, 2);
        var (_, _, otherTeams) = await SeedDisciplineAsync(db, 1);
        var service = new PhaseSetupService(db);
        var phaseId = await service.CreatePhaseAsync(editionId, disciplineId, new PhaseInput { Name = "Skupiny", Type = PhaseType.Group, Order = 1 });
        var groupId = await service.CreateGroupAsync(editionId, disciplineId, phaseId, new PhaseGroupInput { Name = "A", Order = 1 });

        await Assert.ThrowsAsync<ValidationException>(() => service.AssignGroupTeamsAsync(editionId, disciplineId, phaseId, groupId, otherTeams));
    }

    [Fact]
    public async Task DeleteGroup_AllowsOnlyEmptyStage()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 2, PlayingSystemType.Knockout);
        var service = new PhaseSetupService(db);
        var setup = await service.GetSetupAsync(editionId, disciplineId);
        var phase = Assert.Single(setup!.Phases);
        var emptyStageId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Předkolo", Order = 1, Capacity = 2 });

        Assert.True(await service.DeleteGroupAsync(editionId, disciplineId, phase.Id, emptyStageId));

        var assignedStageId = await service.CreateGroupAsync(editionId, disciplineId, phase.Id,
            new PhaseGroupInput { Name = "Finále", Order = 2, Capacity = 2 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, phase.Id, assignedStageId, teamIds);
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.DeleteGroupAsync(editionId, disciplineId, phase.Id, assignedStageId));
    }

    [Fact]
    public async Task DeletePhase_CustomSystem_AllowsOnlyPhaseWithoutAssignedTeams()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 2, PlayingSystemType.Custom);
        var service = new PhaseSetupService(db);
        var emptyPhaseId = await service.CreatePhaseAsync(editionId, disciplineId,
            new PhaseInput { Name = "Prázdná", Type = PhaseType.Group, Order = 1 });
        await service.CreateGroupAsync(editionId, disciplineId, emptyPhaseId,
            new PhaseGroupInput { Name = "A", Order = 1 });

        Assert.True(await service.DeletePhaseAsync(editionId, disciplineId, emptyPhaseId));

        var assignedPhaseId = await service.CreatePhaseAsync(editionId, disciplineId,
            new PhaseInput { Name = "Obsazená", Type = PhaseType.Group, Order = 2 });
        var groupId = await service.CreateGroupAsync(editionId, disciplineId, assignedPhaseId,
            new PhaseGroupInput { Name = "B", Order = 1 });
        await service.AssignGroupTeamsAsync(editionId, disciplineId, assignedPhaseId, groupId, teamIds);
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.DeletePhaseAsync(editionId, disciplineId, assignedPhaseId));
    }

    [Fact]
    public async Task CheckboxAssignment_RedirectsBackToFocusedControl()
    {
        await using var db = CreateDbContext();
        var (editionId, disciplineId, teamIds) = await SeedDisciplineAsync(db, 2, PlayingSystemType.Custom);
        var service = new PhaseSetupService(db);
        var phaseId = await service.CreatePhaseAsync(editionId, disciplineId,
            new PhaseInput { Name = "Skupina", Type = PhaseType.Group, Order = 1 });
        var groupId = await service.CreateGroupAsync(editionId, disciplineId, phaseId,
            new PhaseGroupInput { Name = "A", Order = 1 });
        var page = new DisciplinePhasesModel(service, new GroupStandingsService(db))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin")], "test"))
                }
            }
        };

        var result = await page.OnPostAssignGroupTeamsAsync(
            editionId, disciplineId, phaseId, groupId, [teamIds[0]], teamIds[0], default);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal($"group-{groupId}-team-{teamIds[0]}", redirect.Fragment);
    }

    private static async Task<(long EditionId, long DisciplineId, List<long> TeamIds)> SeedDisciplineAsync(
        CompetitionDbContext db, int teamCount, PlayingSystemType playingSystem = PlayingSystemType.Custom)
    {
        var edition = new CompetitionEdition { Name = $"Cup {Guid.NewGuid()}", City = "Praha", StartDate = new(2026, 8, 22), EndDate = new(2026, 8, 23), CreationToken = Guid.NewGuid() };
        var discipline = new CompetitionDiscipline { CompetitionEdition = edition, Discipline = new Discipline { Name = $"Sport {Guid.NewGuid()}" }, Order = 1, TeamSize = 1, PlayingSystem = playingSystem };
        db.Add(discipline);
        for (var index = 1; index <= teamCount; index++) discipline.Teams.Add(new DisciplineTeam { Seed = index });
        await db.SaveChangesAsync();
        return (edition.Id, discipline.Id, discipline.Teams.OrderBy(x => x.Seed).Select(x => x.Id).ToList());
    }

    private static CompetitionDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CompetitionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class PairComparer : IEqualityComparer<long[]>
    {
        public bool Equals(long[]? x, long[]? y) => x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(long[] obj) => HashCode.Combine(obj[0], obj[1]);
    }
}
