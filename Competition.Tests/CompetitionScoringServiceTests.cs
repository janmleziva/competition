using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class CompetitionScoringServiceTests
{
    [Fact]
    public async Task Finalize_SnapshotsTeamPointsForEveryMemberAndClosesDiscipline()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var pointService = new AwardPointSystemService(db);
        var pointSystemId = await pointService.CreateAsync(new AwardPointSystemInput
        {
            Name = "6-4",
            Rules = [new() { Rank = 1, Points = 6 }, new() { Rank = 2, Points = 4 }]
        });
        var service = CreateService(db, pointService);
        await service.SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystemId);

        Assert.True(await service.FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId));

        var discipline = await db.CompetitionDisciplines.SingleAsync();
        Assert.True(discipline.IsClosed);
        Assert.True(discipline.IsLocked);
        Assert.True(discipline.IsScheduleLocked);
        Assert.NotNull(discipline.ClosedAtUtc);
        Assert.Equal(new[] { 6, 4 }, await db.DisciplineStandings.OrderBy(x => x.Rank)
            .Select(x => x.PointsAwarded).ToArrayAsync());

        var overall = (await service.GetEditionOverallStandingAsync(seeded.EditionId))!;
        Assert.Equal(6, overall.Rows.Single(x => x.EntryId == seeded.FirstWinnerEntryId).TotalPoints);
        Assert.Equal(6, overall.Rows.Single(x => x.EntryId == seeded.SecondWinnerEntryId).TotalPoints);
        Assert.Equal(4, overall.Rows.Single(x => x.EntryId == seeded.LoserEntryId).TotalPoints);

        var finalTable = await new GroupStandingsService(db)
            .GetFinalStandingsAsync(seeded.EditionId, seeded.DisciplineId);
        Assert.True(finalTable!.IsFinalized);
        Assert.Equal(new int?[] { 6, 4 }, finalTable.Rows.Select(x => x.PointsAwarded));
        Assert.Equal("skupinová tabulka", finalTable.Rows[0].DecidedBy);
        Assert.Equal((1, 0), (finalTable.Rows[0].ScoreFor, finalTable.Rows[0].ScoreAgainst));
        Assert.Equal((0, 1), (finalTable.Rows[1].ScoreFor, finalTable.Rows[1].ScoreAgainst));

        await Assert.ThrowsAsync<ValidationException>(() => new DisciplineAdministrationService(db)
            .SetLockAsync(seeded.EditionId, seeded.DisciplineId, false));
    }

    [Fact]
    public async Task Finalize_RejectsIncompleteMatchesAndLeavesDisciplineOpen()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var match = await db.Matches.SingleAsync();
        match.Status = MatchStatus.Scheduled;
        match.HomeScore = null;
        match.AwayScore = null;
        var pointSystem = new AwardPointSystem { Name = "Body" };
        pointSystem.Rules.Add(new RankingPointRule { Rank = 1, Points = 2 });
        pointSystem.Rules.Add(new RankingPointRule { Rank = 2, Points = 1 });
        db.AwardPointSystems.Add(pointSystem);
        await db.SaveChangesAsync();
        await CreateService(db).SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystem.Id);

        await Assert.ThrowsAsync<ValidationException>(() => CreateService(db)
            .FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId));

        Assert.False((await db.CompetitionDisciplines.SingleAsync()).IsClosed);
        Assert.Empty(await db.DisciplineStandings.ToListAsync());
    }

    [Fact]
    public async Task Finalize_AwardsZeroForPlacementsMissingFromPointSystem()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var pointService = new AwardPointSystemService(db);
        var pointSystemId = await pointService.CreateAsync(new AwardPointSystemInput
        {
            Name = "Jen vítěz",
            Rules = [new() { Rank = 1, Points = 6 }]
        });
        var service = CreateService(db, pointService);
        await service.SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystemId);

        var setup = await service.GetDisciplineSetupAsync(seeded.EditionId, seeded.DisciplineId);
        Assert.True(setup!.CanFinalize);
        Assert.True(await service.FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId));

        Assert.Equal(new[] { 6, 0 }, await db.DisciplineStandings.OrderBy(x => x.Rank)
            .Select(x => x.PointsAwarded).ToArrayAsync());
        var overall = (await service.GetEditionOverallStandingAsync(seeded.EditionId))!;
        Assert.Equal(0, overall.Rows.Single(x => x.EntryId == seeded.LoserEntryId).TotalPoints);
    }

    [Fact]
    public async Task Reopen_PreservesAwardedPointsUntilTheyAreExplicitlyRemoved()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var pointService = new AwardPointSystemService(db);
        var pointSystemId = await pointService.CreateAsync(new AwardPointSystemInput
        {
            Name = "Body",
            Rules = [new() { Rank = 1, Points = 3 }]
        });
        var service = CreateService(db, pointService);
        await service.SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystemId);
        await service.FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RemoveAwardedPointsAsync(seeded.EditionId, seeded.DisciplineId));
        Assert.True(await service.ReopenDisciplineAsync(seeded.EditionId, seeded.DisciplineId));

        var reopened = await db.CompetitionDisciplines.Include(x => x.FinalStandings).SingleAsync();
        Assert.False(reopened.IsClosed);
        Assert.Null(reopened.ClosedAtUtc);
        Assert.True(reopened.IsLocked);
        Assert.Equal(2, reopened.FinalStandings.Count);
        Assert.False((await service.GetDisciplineSetupAsync(seeded.EditionId, seeded.DisciplineId))!.CanFinalize);
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId));

        Assert.True(await service.RemoveAwardedPointsAsync(seeded.EditionId, seeded.DisciplineId));
        Assert.Empty(await db.DisciplineStandings.ToListAsync());
        Assert.True((await service.GetDisciplineSetupAsync(seeded.EditionId, seeded.DisciplineId))!.CanFinalize);
    }

    [Fact]
    public async Task CloseWithAwardedPoints_ClosesReopenedDisciplineWithoutChangingPoints()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var pointService = new AwardPointSystemService(db);
        var pointSystemId = await pointService.CreateAsync(new AwardPointSystemInput
        {
            Name = "Body pro opětovné uzavření",
            Rules = [new() { Rank = 1, Points = 6 }, new() { Rank = 2, Points = 3 }]
        });
        var service = CreateService(db, pointService);
        await service.SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystemId);
        await service.FinalizeDisciplineAsync(seeded.EditionId, seeded.DisciplineId);
        var originalPoints = await db.DisciplineStandings.OrderBy(x => x.Rank)
            .Select(x => x.PointsAwarded).ToArrayAsync();
        await service.ReopenDisciplineAsync(seeded.EditionId, seeded.DisciplineId);

        Assert.True(await service.CloseDisciplineWithAwardedPointsAsync(seeded.EditionId, seeded.DisciplineId));

        var discipline = await db.CompetitionDisciplines.SingleAsync();
        Assert.True(discipline.IsClosed);
        Assert.True(discipline.IsLocked);
        Assert.True(discipline.IsScheduleLocked);
        Assert.NotNull(discipline.ClosedAtUtc);
        Assert.Equal(originalPoints, await db.DisciplineStandings.OrderBy(x => x.Rank)
            .Select(x => x.PointsAwarded).ToArrayAsync());
    }

    [Fact]
    public async Task CloseWithAwardedPoints_RejectsForgedRequestWithoutPoints()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CloseDisciplineWithAwardedPointsAsync(seeded.EditionId, seeded.DisciplineId));

        Assert.False((await db.CompetitionDisciplines.SingleAsync()).IsClosed);
    }

    [Fact]
    public async Task OverallStanding_UsesBestPlacementCountsAsTieBreaker()
    {
        await using var db = CreateDbContext();
        var edition = new CompetitionEdition
        {
            Name = "Tie break", City = "Praha", StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2), CreationToken = Guid.NewGuid()
        };
        var entries = new[] { "Dvě třetí", "Jedno třetí", "Tři čtvrtá" }
            .Select((name, index) => new CompetitionEntry
            {
                CompetitionEdition = edition,
                Competitor = new Competitor { FirstName = name, LastName = "Test" },
                Seed = index + 1
            }).ToArray();
        db.AddRange(entries);
        await db.SaveChangesAsync();

        await AddClosedResultAsync(db, edition.Id, "A", 1, entries[0], 3, 2);
        await AddClosedResultAsync(db, edition.Id, "B", 2, entries[0], 3, 2);
        await AddClosedResultAsync(db, edition.Id, "C", 3, entries[1], 3, 4);
        await AddClosedResultAsync(db, edition.Id, "D", 4, entries[2], 4, 1);
        await AddClosedResultAsync(db, edition.Id, "E", 5, entries[2], 4, 1);
        await AddClosedResultAsync(db, edition.Id, "F", 6, entries[2], 4, 2);

        var overall = (await CreateService(db).GetEditionOverallStandingAsync(edition.Id))!;

        Assert.Collection(overall.Rows,
            row => Assert.Equal(entries[0].Id, row.EntryId),
            row => Assert.Equal(entries[1].Id, row.EntryId),
            row => Assert.Equal(entries[2].Id, row.EntryId));
        Assert.All(overall.Rows, row => Assert.Equal(4, row.TotalPoints));
        Assert.Equal(new[] { 1, 2, 3 }, overall.Rows.Select(x => x.Place));
    }

    [Fact]
    public async Task OverallStanding_ShowsPartialEditionNonparticipantsAndSharedPlaces()
    {
        await using var db = CreateDbContext();
        var edition = new CompetitionEdition
        {
            Name = "Partial", City = "Praha", StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2), CreationToken = Guid.NewGuid()
        };
        var entries = new[] { "Anna", "Bara", "Cyril", "Dana" }.Select((name, index) =>
            new CompetitionEntry
            {
                CompetitionEdition = edition,
                Competitor = new Competitor { FirstName = name, LastName = $"Player{index + 1}" },
                Seed = index + 1
            }).ToArray();
        db.AddRange(entries);
        await db.SaveChangesAsync();

        await AddClosedTeamResultAsync(db, edition.Id, "Doubles", 1, [entries[0], entries[1]], 1, 10);
        await AddClosedTeamResultAsync(db, edition.Id, "Singles", 2, [entries[2]], 1, 10);
        db.CompetitionDisciplines.Add(new CompetitionDiscipline
        {
            CompetitionEditionId = edition.Id,
            Discipline = new Discipline { Name = "Pending" },
            PlayingSystem = PlayingSystemType.RoundRobin,
            TeamSize = 1,
            Order = 3,
            IsClosed = false
        });
        await db.SaveChangesAsync();

        var overall = (await CreateService(db).GetEditionOverallStandingAsync(edition.Id))!;

        Assert.Equal(new[] { false }, overall.Disciplines.Where(x => x.Name == "Pending").Select(x => x.IsClosed));
        Assert.Equal(10, overall.Rows.Single(x => x.EntryId == entries[0].Id).TotalPoints);
        Assert.Equal(10, overall.Rows.Single(x => x.EntryId == entries[1].Id).TotalPoints);
        Assert.Equal(10, overall.Rows.Single(x => x.EntryId == entries[2].Id).TotalPoints);
        Assert.Equal(0, overall.Rows.Single(x => x.EntryId == entries[3].Id).TotalPoints);
        Assert.Equal(1, overall.Rows.Single(x => x.EntryId == entries[0].Id).Place);
        Assert.Equal(1, overall.Rows.Single(x => x.EntryId == entries[1].Id).Place);
        Assert.Equal(1, overall.Rows.Single(x => x.EntryId == entries[2].Id).Place);
        Assert.Empty(overall.Rows.Single(x => x.EntryId == entries[3].Id).Disciplines);
        Assert.Equal("Player1/Player2", overall.Rows.Single(x => x.EntryId == entries[0].Id)
            .Disciplines.Single().Value.TeamName);
    }

    [Fact]
    public async Task OverallStanding_RecalculatesAfterAwardedPointsAreAmended()
    {
        await using var db = CreateDbContext();
        var edition = new CompetitionEdition
        {
            Name = "Amended", City = "Praha", StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2), CreationToken = Guid.NewGuid()
        };
        var entries = new[] { "Anna", "Bara" }.Select((name, index) => new CompetitionEntry
        {
            CompetitionEdition = edition,
            Competitor = new Competitor { FirstName = name, LastName = $"Player{index + 1}" },
            Seed = index + 1
        }).ToArray();
        db.AddRange(entries);
        await db.SaveChangesAsync();
        await AddClosedTeamResultAsync(db, edition.Id, "Singles", 1, [entries[0]], 1, 5);

        var standing = await db.DisciplineStandings.SingleAsync();
        standing.PointsAwarded = 8;
        await db.SaveChangesAsync();

        var overall = (await CreateService(db).GetEditionOverallStandingAsync(edition.Id))!;

        Assert.Equal(8, overall.Rows.Single(x => x.EntryId == entries[0].Id).TotalPoints);
        Assert.Equal(0, overall.Rows.Single(x => x.EntryId == entries[1].Id).TotalPoints);
    }

    [Fact]
    public async Task AwardPointSystem_RequiresContinuousUniqueRanks()
    {
        await using var db = CreateDbContext();
        var service = new AwardPointSystemService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new AwardPointSystemInput
        {
            Name = "Neplatný",
            Rules = [new() { Rank = 1, Points = 5 }, new() { Rank = 3, Points = 1 }]
        }));
    }

    [Fact]
    public async Task SetPointSystem_RejectsLockedDiscipline()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var discipline = await db.CompetitionDisciplines.SingleAsync();
        discipline.IsLocked = true;
        var pointSystem = new AwardPointSystem { Name = "Body" };
        pointSystem.Rules.Add(new RankingPointRule { Rank = 1, Points = 2 });
        db.AwardPointSystems.Add(pointSystem);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => CreateService(db)
            .SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, pointSystem.Id));

        Assert.Null((await db.CompetitionDisciplines.SingleAsync()).AwardPointSystemId);
    }

    [Fact]
    public async Task AwardPointSystem_InUseCannotBeUpdatedAndCopyRemainsIndependent()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedCompletedRoundRobinAsync(db);
        var pointSystems = new AwardPointSystemService(db);
        var originalId = await pointSystems.CreateAsync(new AwardPointSystemInput
        {
            Name = "Sdílené body",
            Rules = [new() { Rank = 1, Points = 6 }, new() { Rank = 2, Points = 4 }]
        });
        await CreateService(db, pointSystems).SetPointSystemAsync(seeded.EditionId, seeded.DisciplineId, originalId);
        var changedInput = new AwardPointSystemInput
        {
            Name = "Sdílené body",
            Rules = [new() { Rank = 1, Points = 10 }, new() { Rank = 2, Points = 5 }]
        };

        await Assert.ThrowsAsync<ValidationException>(() => pointSystems.UpdateAsync(originalId, changedInput));
        var copyId = await pointSystems.CreateCopyAsync(originalId, changedInput);

        Assert.NotNull(copyId);
        Assert.NotEqual(originalId, copyId);
        var systems = await pointSystems.ListAsync();
        Assert.Equal(new[] { 6, 4 }, systems.Single(x => x.Id == originalId).Rules.Select(x => x.Points));
        var copy = systems.Single(x => x.Id == copyId);
        Assert.Equal("Sdílené body – kopie", copy.Name);
        Assert.Equal(new[] { 10, 5 }, copy.Rules.Select(x => x.Points));
    }

    private static ICompetitionScoringService CreateService(
        CompetitionDbContext db, IAwardPointSystemService? pointSystems = null) =>
        new CompetitionScoringService(db, new GroupStandingsService(db), pointSystems ?? new AwardPointSystemService(db));

    private static async Task<SeededDiscipline> SeedCompletedRoundRobinAsync(CompetitionDbContext db)
    {
        var edition = new CompetitionEdition
        {
            Name = "2026", City = "Praha", StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2), CreationToken = Guid.NewGuid()
        };
        var entries = new[] { "Anna", "Bára", "Cyril" }.Select((name, index) => new CompetitionEntry
        {
            CompetitionEdition = edition,
            Competitor = new Competitor { FirstName = name, LastName = $"Hráč {index + 1}" },
            Seed = index + 1
        }).ToArray();
        var discipline = new CompetitionDiscipline
        {
            CompetitionEdition = edition,
            Discipline = new Discipline { Name = "Čtyřhra" },
            PlayingSystem = PlayingSystemType.RoundRobin,
            TeamSize = 2,
            Order = 1
        };
        var winner = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = 1 };
        winner.Members.Add(new DisciplineTeamMember { CompetitionDisciplineId = discipline.Id, CompetitionEntry = entries[0], Order = 1 });
        winner.Members.Add(new DisciplineTeamMember { CompetitionDisciplineId = discipline.Id, CompetitionEntry = entries[1], Order = 2 });
        var loser = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = 2 };
        loser.Members.Add(new DisciplineTeamMember { CompetitionDisciplineId = discipline.Id, CompetitionEntry = entries[2], Order = 1 });
        discipline.Teams.Add(winner);
        discipline.Teams.Add(loser);
        var phase = new DisciplinePhase
        {
            CompetitionDiscipline = discipline, Name = "Skupina", Type = PhaseType.Group,
            Order = 1, PointsForWin = 2, PointsForDraw = 1, PointsForLoss = 0
        };
        var group = new PhaseGroup { DisciplinePhase = phase, Name = "Skupina", Order = 1 };
        discipline.Phases.Add(phase);
        phase.Groups.Add(group);
        group.Teams.Add(new PhaseGroupTeam { DisciplinePhaseId = phase.Id, DisciplineTeam = winner, Seed = 1 });
        group.Teams.Add(new PhaseGroupTeam { DisciplinePhaseId = phase.Id, DisciplineTeam = loser, Seed = 2 });
        var match = new Match
        {
            DisciplinePhase = phase, PhaseGroup = group, HomeTeam = winner, AwayTeam = loser,
            Name = "Finále", Order = 1, Status = MatchStatus.Completed, HomeScore = 1, AwayScore = 0
        };
        phase.Matches.Add(match);
        group.Matches.Add(match);
        db.AddRange(entries);
        db.Add(discipline);
        await db.SaveChangesAsync();
        return new SeededDiscipline(edition.Id, discipline.Id, entries[0].Id, entries[1].Id, entries[2].Id);
    }

    private static async Task AddClosedResultAsync(
        CompetitionDbContext db, long editionId, string name, int order,
        CompetitionEntry entry, int rank, int points)
    {
        await AddClosedTeamResultAsync(db, editionId, name, order, [entry], rank, points);
    }

    private static async Task AddClosedTeamResultAsync(
        CompetitionDbContext db, long editionId, string name, int order,
        IReadOnlyList<CompetitionEntry> entries, int rank, int points)
    {
        var discipline = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId, Discipline = new Discipline { Name = name },
            PlayingSystem = PlayingSystemType.RoundRobin, TeamSize = entries.Count, Order = order, IsClosed = true
        };
        var team = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = 1 };
        for (var index = 0; index < entries.Count; index++)
        {
            team.Members.Add(new DisciplineTeamMember
            {
                CompetitionDisciplineId = discipline.Id,
                CompetitionEntry = entries[index],
                Order = index + 1
            });
        }
        discipline.Teams.Add(team);
        discipline.FinalStandings.Add(new DisciplineStanding
        {
            CompetitionDiscipline = discipline, DisciplineTeam = team, Rank = rank, PointsAwarded = points
        });
        db.Add(discipline);
        await db.SaveChangesAsync();
    }

    private static CompetitionDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CompetitionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record SeededDiscipline(
        long EditionId, long DisciplineId, long FirstWinnerEntryId, long SecondWinnerEntryId, long LoserEntryId);
}
