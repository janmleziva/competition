using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Competition.Pages.Editions;
using Competition.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class DisciplineAdministrationServiceTests
{
    [Fact]
    public async Task CatalogAndEditionSetup_PersistsScheduleAndTeamSize()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var service = new DisciplineAdministrationService(db);
        var catalogId = await service.CreateCatalogAsync(new DisciplineCatalogInput { Name = " Beach volejbal " });

        await service.AttachAsync(editionId, new EditionDisciplineInput
        {
            DisciplineId = catalogId, Order = 1, TeamSize = 2,
            PlayingSystem = PlayingSystemType.RoundRobinThenKnockout,
            UsesSetScores = true,
            SetsToWin = 2,
            ScheduledAt = new DateTime(2026, 8, 22, 14, 30, 0)
        });

        var configured = Assert.Single((await service.GetEditionSetupAsync(editionId))!.Disciplines);
        Assert.Equal("Beach volejbal", configured.Name);
        Assert.Equal(2, configured.TeamSize);
        Assert.True(configured.UsesSetScores);
        Assert.Equal(2, configured.SetsToWin);
        Assert.Equal(new DateTime(2026, 8, 22, 14, 30, 0), configured.ScheduledAt);
    }

    [Fact]
    public async Task AssignAll_GroupsCompetitorsByEditionSeed()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 4);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.AssignAllAsync(editionId, disciplineId));

        var assignments = await db.DisciplineParticipantAssignments.OrderBy(x => x.CompetitionEntryId).ToListAsync();
        Assert.Equal(4, assignments.Count);
        Assert.Empty(await db.DisciplineTeams.ToListAsync());
    }

    [Fact]
    public async Task UpdateParticipants_AllowsInjuryReplacementWithoutForcingTeams()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 4);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var service = new DisciplineAdministrationService(db);
        var entryIds = await db.CompetitionEntries.OrderBy(x => x.Seed).Select(x => x.Id).ToListAsync();
        await service.UpdateParticipantsAsync(editionId, disciplineId, entryIds.Take(2).ToArray());

        await service.UpdateParticipantsAsync(editionId, disciplineId, new[] { entryIds[1], entryIds[2] });

        var assigned = await db.DisciplineParticipantAssignments.OrderBy(x => x.CompetitionEntryId).Select(x => x.CompetitionEntryId).ToListAsync();
        Assert.Equal(new[] { entryIds[1], entryIds[2] }, assigned);
        Assert.Empty(await db.DisciplineTeamMembers.ToListAsync());
    }

    [Fact]
    public async Task CreateTeam_UsesOnlyAssignedAndUnteamedCompetitors()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 4);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var service = new DisciplineAdministrationService(db);
        var entryIds = await db.CompetitionEntries.OrderBy(x => x.Seed).Select(x => x.Id).ToListAsync();
        await service.UpdateParticipantsAsync(editionId, disciplineId, entryIds, default);

        await service.CreateTeamAsync(editionId, disciplineId, new[] { entryIds[0], entryIds[1] });

        var team = await db.DisciplineTeams.Include(x => x.Members).SingleAsync();
        Assert.Equal(new[] { entryIds[0], entryIds[1] }, team.Members.OrderBy(x => x.Order).Select(x => x.CompetitionEntryId).ToArray());
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTeamAsync(editionId, disciplineId, new[] { entryIds[1], entryIds[2] }));
    }

    [Fact]
    public async Task Update_SwapsDisciplineOrdersWhenRequestedOrderIsOccupied()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var first = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId,
            Discipline = new Discipline { Name = "Tenis" },
            Order = 1,
            TeamSize = 2
        };
        var second = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId,
            Discipline = new Discipline { Name = "Padel" },
            Order = 2,
            TeamSize = 2
        };
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.UpdateAsync(editionId, first.Id, new EditionDisciplineInput
        {
            DisciplineId = first.DisciplineId,
            Order = 2,
            TeamSize = 2
        }));

        Assert.Equal(2, first.Order);
        Assert.Equal(1, second.Order);
    }

    [Fact]
    public async Task Update_PersistsSetScoreOption()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var configured = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = configured.DisciplineId,
            Order = configured.Order,
            TeamSize = configured.TeamSize,
            PlayingSystem = configured.PlayingSystem,
            UsesSetScores = true,
            SetsToWin = 2
        }));

        Assert.True((await service.GetEditionSetupAsync(editionId))!.Disciplines.Single().UsesSetScores);
    }

    [Fact]
    public async Task Update_PlayingSystemChangeClearsPhaseAssignmentsBeforeMatchesExist()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 2);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1, PlayingSystemType.Knockout);
        var discipline = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        var team = new DisciplineTeam { CompetitionDisciplineId = disciplineId, Seed = 1 };
        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = disciplineId,
            Name = "Předkolo",
            Type = PhaseType.Knockout,
            Order = 1
        };
        var group = new PhaseGroup
        {
            DisciplinePhase = phase,
            Name = "Předkolo",
            Order = 1,
            Capacity = 2
        };
        phase.Groups.Add(group);
        group.Teams.Add(new PhaseGroupTeam
        {
            DisciplinePhaseId = phase.Id,
            PhaseGroup = group,
            DisciplineTeam = team,
            Seed = 1
        });
        db.AddRange(team, phase);
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        Assert.True((await service.GetDetailAsync(editionId, disciplineId))!.Discipline.HasPhaseAssignments);
        Assert.True(await service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = discipline.DisciplineId,
            Order = discipline.Order,
            TeamSize = discipline.TeamSize,
            PlayingSystem = PlayingSystemType.RoundRobin,
            UsesSetScores = discipline.UsesSetScores,
            SetsToWin = discipline.SetsToWin
        }));

        Assert.Equal(PlayingSystemType.RoundRobin, (await db.CompetitionDisciplines.SingleAsync()).PlayingSystem);
        Assert.Empty(await db.DisciplinePhases.ToListAsync());
        Assert.Empty(await db.PhaseGroupTeams.ToListAsync());
    }

    [Fact]
    public async Task Update_PlayingSystemChangeIsRejectedWhenScheduleIsLocked()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1, PlayingSystemType.Knockout);
        var discipline = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        discipline.IsScheduleLocked = true;
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = discipline.DisciplineId,
            Order = discipline.Order,
            TeamSize = discipline.TeamSize,
            PlayingSystem = PlayingSystemType.RoundRobin
        }));
    }

    [Fact]
    public async Task Update_SetScoringSettingsAreRejectedWhenResultsExist()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1);
        var discipline = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        discipline.UsesSetScores = true;
        discipline.SetsToWin = 2;
        db.Matches.Add(new Match
        {
            DisciplinePhase = new DisciplinePhase
            {
                CompetitionDisciplineId = disciplineId,
                Name = "Skupina",
                Type = PhaseType.Group,
                Order = 1
            },
            Name = "Zápas",
            Order = 1,
            HomeScore = 2,
            AwayScore = 0,
            Status = MatchStatus.Completed
        });
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = discipline.DisciplineId,
            Order = discipline.Order,
            TeamSize = discipline.TeamSize,
            PlayingSystem = discipline.PlayingSystem,
            UsesSetScores = true,
            SetsToWin = 3
        }));

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = discipline.DisciplineId,
            Order = discipline.Order,
            TeamSize = discipline.TeamSize,
            PlayingSystem = discipline.PlayingSystem,
            UsesSetScores = false
        }));
    }

    [Fact]
    public async Task DisciplineLock_BlocksConfigurationUpdatesUntilUnlocked()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var configured = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.SetLockAsync(editionId, disciplineId, true));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = configured.DisciplineId,
            Order = configured.Order,
            TeamSize = configured.TeamSize,
            PlayingSystem = configured.PlayingSystem,
            Description = "Nový popis"
        }));

        var lockedDetail = await service.GetDetailAsync(editionId, disciplineId);
        Assert.True(lockedDetail!.Discipline.IsLocked);

        Assert.True(await service.SetLockAsync(editionId, disciplineId, false));
        Assert.True(await service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = configured.DisciplineId,
            Order = configured.Order,
            TeamSize = configured.TeamSize,
            PlayingSystem = configured.PlayingSystem,
            Description = " Nový popis "
        }));
        Assert.Equal("Nový popis", (await service.GetDetailAsync(editionId, disciplineId))!.Discipline.Description);
    }

    [Fact]
    public async Task Remove_DisciplineWithResultsIsRejectedUntilResultsAreCleared()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1);
        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = disciplineId,
            Name = "Skupina",
            Type = PhaseType.Group,
            Order = 1
        };
        phase.Matches.Add(new Match
        {
            Name = "Zápas",
            Order = 1,
            Status = MatchStatus.Completed,
            HomeScore = 1,
            AwayScore = 0
        });
        db.Add(phase);
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);
        await service.SetLockAsync(editionId, disciplineId, true);

        await Assert.ThrowsAsync<ValidationException>(() => service.RemoveAsync(editionId, disciplineId));
        Assert.False((await service.GetEditionSetupAsync(editionId))!.Disciplines.Single().CanRemove);

        await service.SetLockAsync(editionId, disciplineId, false);
        await Assert.ThrowsAsync<ValidationException>(() => service.RemoveAsync(editionId, disciplineId));
        var match = await db.Matches.SingleAsync();
        match.HomeScore = null;
        match.AwayScore = null;
        match.Status = MatchStatus.Scheduled;
        await db.SaveChangesAsync();
        Assert.True(await service.RemoveAsync(editionId, disciplineId));
        Assert.Empty(await db.CompetitionDisciplines.ToListAsync());
        Assert.Empty(await db.Matches.ToListAsync());
    }

    [Fact]
    public async Task Remove_ScheduleLockedDisciplineIsRejectedEvenWithoutResults()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1);
        (await db.CompetitionDisciplines.SingleAsync()).IsScheduleLocked = true;
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        var setup = await service.GetEditionSetupAsync(editionId);
        Assert.False(Assert.Single(setup!.Disciplines).CanRemove);
        await Assert.ThrowsAsync<ValidationException>(() => service.RemoveAsync(editionId, disciplineId));
    }

    [Fact]
    public async Task ParticipantsCanChangeWithPresetPhasesButNotAfterScheduleLock()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 2);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1);
        db.DisciplinePhases.Add(new DisciplinePhase
        {
            CompetitionDisciplineId = disciplineId,
            Name = "Detaily",
            Type = PhaseType.Group,
            Order = 1
        });
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.AssignAllAsync(editionId, disciplineId));
        (await db.CompetitionDisciplines.SingleAsync()).IsScheduleLocked = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateParticipantsAsync(
            editionId, disciplineId, []));
    }

    [Fact]
    public async Task ParticipantsAndTeamsCannotChangeAfterMatchesExist()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 2);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 1);
        var service = new DisciplineAdministrationService(db);

        Assert.True(await service.AssignAllAsync(editionId, disciplineId));
        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = disciplineId,
            Name = "Detaily",
            Type = PhaseType.Group,
            Order = 1
        };
        phase.Matches.Add(new Match
        {
            Name = "Zápas",
            Order = 1
        });
        db.DisciplinePhases.Add(phase);
        await db.SaveChangesAsync();
        var teamId = await db.DisciplineTeams
            .Where(x => x.CompetitionDisciplineId == disciplineId)
            .Select(x => x.Id)
            .FirstAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateParticipantsAsync(
            editionId, disciplineId, []));
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteTeamAsync(
            editionId, disciplineId, teamId));
    }

    [Fact]
    public async Task DeleteTeam_ClearsPhaseAssignmentBeforeMatchesExist()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 4);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var service = new DisciplineAdministrationService(db);
        var entryIds = await db.CompetitionEntries
            .OrderBy(x => x.Seed)
            .Select(x => x.Id)
            .ToListAsync();
        await service.UpdateParticipantsAsync(editionId, disciplineId, entryIds);
        await service.CreateTeamAsync(editionId, disciplineId, entryIds.Take(2).ToList());
        var teamId = await db.DisciplineTeams
            .Where(x => x.CompetitionDisciplineId == disciplineId)
            .Select(x => x.Id)
            .SingleAsync();
        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = disciplineId,
            Name = "Skupina A",
            Type = PhaseType.Group,
            Order = 1
        };
        var group = new PhaseGroup
        {
            DisciplinePhase = phase,
            Name = "Skupina A",
            Order = 1
        };
        phase.Groups.Add(group);
        db.DisciplinePhases.Add(phase);
        await db.SaveChangesAsync();
        db.PhaseGroupTeams.Add(new PhaseGroupTeam
        {
            DisciplinePhaseId = phase.Id,
            PhaseGroupId = group.Id,
            DisciplineTeamId = teamId,
            Seed = 1
        });
        await db.SaveChangesAsync();

        Assert.True(await service.DeleteTeamAsync(editionId, disciplineId, teamId));

        Assert.Empty(await db.PhaseGroupTeams.ToListAsync());
        Assert.Empty(await db.DisciplineTeams.ToListAsync());
    }

    [Fact]
    public async Task Attach_MovesDisciplineAtRequestedOrderToLowestFreeOrder()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var existing = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId,
            Discipline = new Discipline { Name = "Tenis" },
            Order = 1,
            TeamSize = 2
        };
        var newCatalogDiscipline = new Discipline { Name = "Basket" };
        db.AddRange(existing, newCatalogDiscipline);
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        var attachedId = await service.AttachAsync(editionId, new EditionDisciplineInput
        {
            DisciplineId = newCatalogDiscipline.Id,
            Order = 1,
            TeamSize = 2
        });

        var attached = await db.CompetitionDisciplines.SingleAsync(x => x.Id == attachedId);
        Assert.Equal(1, attached.Order);
        Assert.Equal(2, existing.Order);
    }

    [Fact]
    public async Task Attach_PersistsAwardPointSystem()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var discipline = new Discipline { Name = "Basket" };
        var pointSystem = new AwardPointSystem { Name = "Body" };
        db.AddRange(discipline, pointSystem);
        await db.SaveChangesAsync();
        var service = new DisciplineAdministrationService(db);

        var attachedId = await service.AttachAsync(editionId, new EditionDisciplineInput
        {
            DisciplineId = discipline.Id,
            Order = 1,
            TeamSize = 2,
            AwardPointSystemId = pointSystem.Id
        });

        var attached = await db.CompetitionDisciplines.SingleAsync(x => x.Id == attachedId);
        Assert.Equal(pointSystem.Id, attached.AwardPointSystemId);
        Assert.Equal(pointSystem.Id, (await service.GetEditionSetupAsync(editionId))!.Disciplines.Single().AwardPointSystemId);
    }

    [Fact]
    public async Task Update_RejectsTeamSizeChangeAfterTeamsExist()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 2);
        var disciplineId = await AddDiscipline(db, editionId, teamSize: 2);
        var service = new DisciplineAdministrationService(db);
        var discipline = await db.CompetitionDisciplines.SingleAsync(x => x.Id == disciplineId);
        var entryIds = await db.CompetitionEntries.Select(x => x.Id).ToListAsync();
        await service.UpdateParticipantsAsync(editionId, disciplineId, entryIds);
        await service.CreateTeamAsync(editionId, disciplineId, entryIds);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(editionId, disciplineId, new EditionDisciplineInput
        {
            DisciplineId = discipline.DisciplineId,
            Order = discipline.Order,
            TeamSize = 3
        }));
    }

    [Fact]
    public async Task AttachHandler_IgnoresValidationFromCreateDisciplineForm()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var discipline = new Discipline { Name = "Padel" };
        db.Add(discipline);
        await db.SaveChangesAsync();
        var page = CreateAuthenticatedPage(new DisciplineAdministrationService(db));
        page.Input = new EditionDisciplineInput
        {
            DisciplineId = discipline.Id,
            Order = 1,
            TeamSize = 2
        };
        page.ModelState.AddModelError("Name", "Required");

        var result = await page.OnPostAttachAsync(editionId, default);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        var assigned = Assert.Single(await db.CompetitionDisciplines.ToListAsync());
        Assert.Equal("/Editions/DisciplineDetail", redirect.PageName);
        Assert.Equal(editionId, redirect.RouteValues!["id"]);
        Assert.Equal(assigned.Id, redirect.RouteValues["disciplineId"]);
    }

    [Fact]
    public async Task Get_PrepopulatesLowestFreeOrderAndTeamSizeTwo()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        db.AddRange(
            new CompetitionDiscipline
            {
                CompetitionEditionId = editionId,
                Discipline = new Discipline { Name = "Tenis" },
                Order = 1,
                TeamSize = 1
            },
            new CompetitionDiscipline
            {
                CompetitionEditionId = editionId,
                Discipline = new Discipline { Name = "Padel" },
                Order = 3,
                TeamSize = 2
            });
        await db.SaveChangesAsync();
        var page = CreateAuthenticatedPage(new DisciplineAdministrationService(db));

        var result = await page.OnGetAsync(editionId, default);

        Assert.IsType<PageResult>(result);
        Assert.Equal(2, page.Input.Order);
        Assert.Equal(2, page.Input.TeamSize);
    }

    [Fact]
    public async Task CreateAndSelect_PreservesAssignmentValuesAndClearsNewName()
    {
        await using var db = CreateDbContext();
        var editionId = await SeedEditionWithCompetitors(db, 0);
        var page = CreateAuthenticatedPage(new DisciplineAdministrationService(db));
        page.Input = new EditionDisciplineInput
        {
            Order = 7,
            TeamSize = 4,
            PlayingSystem = PlayingSystemType.Knockout,
            AwardPointSystemId = 42,
            ScheduledAt = new DateTime(2026, 8, 22, 17, 30, 0)
        };
        page.NewDiscipline = new DisciplineCatalogInput { Name = "Basket" };
        page.ModelState.SetModelValue("NewDiscipline.Name", "Basket", "Basket");

        var result = await page.OnPostCreateAndSelectAsync(editionId, default);

        Assert.IsType<PageResult>(result);
        Assert.Equal(7, page.Input.Order);
        Assert.Equal(4, page.Input.TeamSize);
        Assert.Equal(PlayingSystemType.Knockout, page.Input.PlayingSystem);
        Assert.Equal(42, page.Input.AwardPointSystemId);
        Assert.Equal(new DateTime(2026, 8, 22, 17, 30, 0), page.Input.ScheduledAt);
        Assert.True(page.Input.DisciplineId > 0);
        Assert.Empty(page.NewDiscipline.Name);
        Assert.False(page.ModelState.ContainsKey("NewDiscipline.Name"));
    }

    private static async Task<long> SeedEditionWithCompetitors(CompetitionDbContext db, int count)
    {
        var edition = new CompetitionEdition { Name = "Cup", City = "Praha", StartDate = new(2026, 8, 22), EndDate = new(2026, 8, 23), CreationToken = Guid.NewGuid() };
        db.Add(edition); await db.SaveChangesAsync();
        for (var i = 1; i <= count; i++)
        {
            var competitor = new Competitor { FirstName = $"Jméno{i}", LastName = $"Příjmení{i}" };
            db.CompetitionEntries.Add(new CompetitionEntry { CompetitionEditionId = edition.Id, Competitor = competitor, Seed = i });
        }
        await db.SaveChangesAsync(); return edition.Id;
    }

    private static async Task<long> AddDiscipline(
        CompetitionDbContext db,
        long editionId,
        int teamSize,
        PlayingSystemType playingSystem = PlayingSystemType.RoundRobin)
    {
        var configured = new CompetitionDiscipline { CompetitionEditionId = editionId, Discipline = new Discipline { Name = "Tenis" }, TeamSize = teamSize, Order = 1, PlayingSystem = playingSystem };
        db.Add(configured); await db.SaveChangesAsync(); return configured.Id;
    }

    private static CompetitionDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CompetitionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DisciplinesModel CreateAuthenticatedPage(IDisciplineAdministrationService service) =>
        new(service)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin")],
                        "test"))
                }
            }
        };
}
