using Competition.Data;
using Competition.Domain;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class GroupStandingsServiceTests
{
    [Fact]
    public async Task EmptyGroup_ReturnsAnEmptyTable()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, []);
        var service = new GroupStandingsService(db);

        var table = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId));

        Assert.Equal(setup.GroupId, table.GroupId);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task AssignedTeamsWithoutCompletedMatches_AreIncludedWithZeroValues()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta"]);
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.InProgress, 7, 4);
        var service = new GroupStandingsService(db);

        var rows = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Collection(rows,
            row => AssertZeroRow(row, "Alpha", 1),
            row => AssertZeroRow(row, "Beta", 2));
    }

    [Fact]
    public async Task StandingsUseCompetitionTeamSeed_NotGroupAssignmentOrder()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta"]);
        var assignments = await db.PhaseGroupTeams
            .OrderBy(item => item.DisciplineTeamId)
            .ToListAsync();
        assignments[0].Seed = 2;
        assignments[1].Seed = 1;
        await db.SaveChangesAsync();

        var rows = Assert.Single(await new GroupStandingsService(db)
            .GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Collection(rows,
            row => AssertZeroRow(row, "Alpha", 1),
            row => AssertZeroRow(row, "Beta", 2));
    }

    [Fact]
    public async Task CompletedMatches_UsePhasePointRulesAndIgnoreIncompleteMatches()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma", "Delta"]);
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 3, 1);
        await AddMatchAsync(db, setup, 0, 2, MatchStatus.Completed, 2, 2);
        await AddMatchAsync(db, setup, 1, 2, MatchStatus.Completed, 4, 0);
        await AddMatchAsync(db, setup, 2, 3, MatchStatus.InProgress, 8, 1);
        var service = new GroupStandingsService(db);

        var rows = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Collection(rows,
            row => AssertRow(row, "Alpha", 2, 1, 1, 0, 3, 5, 3),
            row => AssertRow(row, "Beta", 2, 1, 0, 1, 2, 5, 3),
            row => AssertRow(row, "Gamma", 2, 0, 1, 1, 1, 2, 6),
            row => AssertZeroRow(row, "Delta", 4));
    }

    [Fact]
    public async Task EqualTablePoints_UseHeadToHeadBeforeOverallScoreDifference()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma"]);
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 1, 0);
        await AddMatchAsync(db, setup, 1, 2, MatchStatus.Completed, 10, 0);
        var service = new GroupStandingsService(db);

        var rows = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Equal(["Alpha", "Beta", "Gamma"], rows.Select(row => row.TeamName));
        Assert.Equal(2, rows[0].TablePoints);
        Assert.Equal(2, rows[1].TablePoints);
        Assert.True(rows[0].ScoreDifference < rows[1].ScoreDifference);
    }

    [Fact]
    public async Task EqualDifferences_UseMoreScoredPointsInsteadOfScoreRatio()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma"]);
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 0, 0);
        await AddMatchAsync(db, setup, 0, 2, MatchStatus.Completed, 5, 3);
        await AddMatchAsync(db, setup, 1, 2, MatchStatus.Completed, 3, 1);

        var rows = Assert.Single(await new GroupStandingsService(db)
            .GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Equal(["Alpha", "Beta", "Gamma"], rows.Select(row => row.TeamName));
        Assert.Equal(rows[0].ScoreDifference, rows[1].ScoreDifference);
        Assert.True(rows[0].ScoreRatio < rows[1].ScoreRatio);
        Assert.True(rows[0].ScoreFor > rows[1].ScoreFor);
    }

    [Fact]
    public async Task ReadingAgainImmediatelyReflectsAnEditedResult()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta"]);
        var match = await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 2, 0);
        var service = new GroupStandingsService(db);

        var before = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;
        Assert.Equal("Alpha", before[0].TeamName);

        match.HomeScore = 0;
        match.AwayScore = 3;
        await db.SaveChangesAsync();

        var after = Assert.Single(await service.GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;
        Assert.Equal("Beta", after[0].TeamName);
        Assert.Equal((3, 0), (after[0].ScoreFor, after[0].ScoreAgainst));
    }

    [Fact]
    public async Task EqualPointsAndMiniScoreDifference_UseMiniSubscoreDifference()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma"], usesSetScores: true);
        var alphaBeta = await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 1, 0);
        var betaGamma = await AddMatchAsync(db, setup, 1, 2, MatchStatus.Completed, 1, 0);
        var gammaAlpha = await AddMatchAsync(db, setup, 2, 0, MatchStatus.Completed, 1, 0);
        db.MatchSetScores.AddRange(
            new MatchSetScore { MatchId = alphaBeta.Id, SetNumber = 1, HomeScore = 10, AwayScore = 9 },
            new MatchSetScore { MatchId = betaGamma.Id, SetNumber = 1, HomeScore = 10, AwayScore = 1 },
            new MatchSetScore { MatchId = gammaAlpha.Id, SetNumber = 1, HomeScore = 10, AwayScore = 0 });
        await db.SaveChangesAsync();

        var rows = Assert.Single(await new GroupStandingsService(db)
            .GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).Rows;

        Assert.Equal(["Beta", "Gamma", "Alpha"], rows.Select(row => row.TeamName));
        Assert.Equal([8, 1, -9], rows.Select(row => row.SubscoreDifference));
        Assert.True(Assert.Single(await new GroupStandingsService(db)
            .GetForDisciplineAsync(setup.EditionId, setup.DisciplineId)).ShowsSubscore);
    }

    [Fact]
    public async Task DisciplineWithoutSets_IgnoresStoredSubscoresWhenOrdering()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma"]);
        var alphaBeta = await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 1, 0);
        var betaGamma = await AddMatchAsync(db, setup, 1, 2, MatchStatus.Completed, 1, 0);
        var gammaAlpha = await AddMatchAsync(db, setup, 2, 0, MatchStatus.Completed, 1, 0);
        db.MatchSetScores.AddRange(
            new MatchSetScore { MatchId = alphaBeta.Id, SetNumber = 1, HomeScore = 10, AwayScore = 9 },
            new MatchSetScore { MatchId = betaGamma.Id, SetNumber = 1, HomeScore = 10, AwayScore = 1 },
            new MatchSetScore { MatchId = gammaAlpha.Id, SetNumber = 1, HomeScore = 10, AwayScore = 0 });
        await db.SaveChangesAsync();

        var table = Assert.Single(await new GroupStandingsService(db)
            .GetForDisciplineAsync(setup.EditionId, setup.DisciplineId));

        Assert.False(table.ShowsSubscore);
        Assert.Equal(["Alpha", "Beta", "Gamma"], table.Rows.Select(row => row.TeamName));
    }

    [Fact]
    public async Task RoundRobinFinalStandings_AppearOnlyAfterEveryMatchIsCompleted()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta"]);
        var match = await AddMatchAsync(db, setup, 0, 1, MatchStatus.InProgress, 2, 0);
        var service = new GroupStandingsService(db);

        Assert.Null(await service.GetFinalStandingsAsync(setup.EditionId, setup.DisciplineId));

        match.Status = MatchStatus.Completed;
        await db.SaveChangesAsync();
        var final = Assert.IsType<FinalStandingTable>(
            await service.GetFinalStandingsAsync(setup.EditionId, setup.DisciplineId));

        Assert.False(final.ShowsSubscore);
        Assert.Equal(["Alpha", "Beta"], final.Rows.Select(row => row.TeamName));
        Assert.Equal([1, 2], final.Rows.Select(row => row.Position));
    }

    [Fact]
    public async Task FinalStandings_AggregateScoresFromGroupAndPlacementMatches()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta"]);
        var discipline = await db.CompetitionDisciplines.SingleAsync(x => x.Id == setup.DisciplineId);
        discipline.PlayingSystem = PlayingSystemType.GroupsThenClassificationMatches;
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 2, 0);
        var finalPhase = new DisciplinePhase
        {
            CompetitionDisciplineId = setup.DisciplineId,
            Name = "O umístění",
            Type = PhaseType.FinalStanding,
            Order = 2
        };
        db.DisciplinePhases.Add(finalPhase);
        await db.SaveChangesAsync();
        db.Matches.Add(new Match
        {
            DisciplinePhaseId = finalPhase.Id,
            HomeTeamId = setup.TeamIds[1],
            AwayTeamId = setup.TeamIds[0],
            Name = "Finále",
            Order = 1,
            Status = MatchStatus.Completed,
            HomeScore = 1,
            AwayScore = 3
        });
        await db.SaveChangesAsync();

        var final = Assert.IsType<FinalStandingTable>(await new GroupStandingsService(db)
            .GetFinalStandingsAsync(setup.EditionId, setup.DisciplineId));

        Assert.Equal("Alpha", final.Rows[0].TeamName);
        Assert.Equal((5, 1), (final.Rows[0].ScoreFor, final.Rows[0].ScoreAgainst));
        Assert.Equal((1, 5), (final.Rows[1].ScoreFor, final.Rows[1].ScoreAgainst));
    }

    [Fact]
    public async Task KnockoutCompletedStage_RanksEliminatedTeamsBeforeLaterStageIsPlayed()
    {
        await using var db = CreateDbContext();
        var setup = await SeedGroupAsync(db, ["Alpha", "Beta", "Gamma", "Delta"]);
        var discipline = await db.CompetitionDisciplines.SingleAsync(item => item.Id == setup.DisciplineId);
        discipline.PlayingSystem = PlayingSystemType.Knockout;
        var phase = await db.DisciplinePhases.SingleAsync(item => item.Id == setup.PhaseId);
        phase.Type = PhaseType.Knockout;
        var semifinal = await db.PhaseGroups.SingleAsync(item => item.Id == setup.GroupId);
        semifinal.Name = "Semifinále";
        semifinal.Capacity = 4;
        await AddMatchAsync(db, setup, 0, 1, MatchStatus.Completed, 3, 0);
        await AddMatchAsync(db, setup, 2, 3, MatchStatus.Completed, 3, 2);

        var finalStage = new PhaseGroup
        {
            DisciplinePhaseId = setup.PhaseId,
            Name = "Finále",
            Order = 2,
            Capacity = 2
        };
        db.PhaseGroups.Add(finalStage);
        await db.SaveChangesAsync();
        db.Matches.Add(new Match
        {
            DisciplinePhaseId = setup.PhaseId,
            PhaseGroupId = finalStage.Id,
            HomeTeamId = setup.TeamIds[0],
            AwayTeamId = setup.TeamIds[2],
            Name = "Finále",
            Order = 1,
            Status = MatchStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var final = Assert.IsType<FinalStandingTable>(await new GroupStandingsService(db)
            .GetFinalStandingsAsync(setup.EditionId, setup.DisciplineId));

        Assert.Equal([3, 4], final.Rows.Select(row => row.Position));
        Assert.Equal(["Delta", "Beta"], final.Rows.Select(row => row.TeamName));
    }

    private static void AssertZeroRow(GroupStandingRow row, string name, int seed) =>
        AssertRow(row, name, 0, 0, 0, 0, 0, 0, 0, seed);

    private static void AssertRow(
        GroupStandingRow row,
        string name,
        int played,
        int wins,
        int draws,
        int losses,
        int points,
        int scoreFor,
        int scoreAgainst,
        int? seed = null)
    {
        Assert.Equal(name, row.TeamName);
        Assert.Equal(played, row.Played);
        Assert.Equal(wins, row.Wins);
        Assert.Equal(draws, row.Draws);
        Assert.Equal(losses, row.Losses);
        Assert.Equal(points, row.TablePoints);
        Assert.Equal(scoreFor, row.ScoreFor);
        Assert.Equal(scoreAgainst, row.ScoreAgainst);
        if (seed is not null)
        {
            Assert.Equal(seed, row.Seed);
        }
    }

    private static async Task<SeededGroup> SeedGroupAsync(
        CompetitionDbContext db,
        IReadOnlyList<string> lastNames,
        bool usesSetScores = false)
    {
        var edition = new CompetitionEdition
        {
            Name = "Cup",
            City = "Praha",
            StartDate = new(2026, 8, 22),
            EndDate = new(2026, 8, 23),
            CreationToken = Guid.NewGuid()
        };
        var discipline = new CompetitionDiscipline
        {
            CompetitionEdition = edition,
            Discipline = new Discipline { Name = $"Sport {Guid.NewGuid()}" },
            PlayingSystem = PlayingSystemType.RoundRobin,
            UsesSetScores = usesSetScores,
            SetsToWin = usesSetScores ? 2 : null,
            TeamSize = 1,
            Order = 1
        };
        db.Add(discipline);

        var entries = lastNames.Select((lastName, index) => new CompetitionEntry
        {
            CompetitionEdition = edition,
            Competitor = new Competitor { FirstName = string.Empty, LastName = lastName },
            Seed = index + 1
        }).ToList();
        db.AddRange(entries);

        var teams = lastNames.Select((_, index) => new DisciplineTeam
        {
            CompetitionDiscipline = discipline,
            Seed = index + 1
        }).ToList();
        db.AddRange(teams);
        await db.SaveChangesAsync();

        for (var index = 0; index < teams.Count; index++)
        {
            db.DisciplineTeamMembers.Add(new DisciplineTeamMember
            {
                CompetitionDisciplineId = discipline.Id,
                DisciplineTeamId = teams[index].Id,
                CompetitionEntryId = entries[index].Id,
                Order = 1
            });
        }

        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = discipline.Id,
            Name = "Skupina",
            Type = PhaseType.Group,
            Order = 1,
            PointsForWin = 2,
            PointsForDraw = 1,
            PointsForLoss = 0
        };
        db.DisciplinePhases.Add(phase);
        await db.SaveChangesAsync();

        var group = new PhaseGroup
        {
            DisciplinePhaseId = phase.Id,
            Name = "A",
            Order = 1
        };
        db.PhaseGroups.Add(group);
        await db.SaveChangesAsync();

        for (var index = 0; index < teams.Count; index++)
        {
            db.PhaseGroupTeams.Add(new PhaseGroupTeam
            {
                DisciplinePhaseId = phase.Id,
                PhaseGroupId = group.Id,
                DisciplineTeamId = teams[index].Id,
                Seed = index + 1
            });
        }
        await db.SaveChangesAsync();

        return new SeededGroup(
            edition.Id,
            discipline.Id,
            phase.Id,
            group.Id,
            teams.Select(team => team.Id).ToList());
    }

    private static async Task<Match> AddMatchAsync(
        CompetitionDbContext db,
        SeededGroup setup,
        int homeIndex,
        int awayIndex,
        MatchStatus status,
        int? homeScore,
        int? awayScore)
    {
        var match = new Match
        {
            DisciplinePhaseId = setup.PhaseId,
            PhaseGroupId = setup.GroupId,
            HomeTeamId = setup.TeamIds[homeIndex],
            AwayTeamId = setup.TeamIds[awayIndex],
            Name = $"Match {await db.Matches.CountAsync() + 1}",
            Order = await db.Matches.CountAsync() + 1,
            Status = status,
            HomeScore = homeScore,
            AwayScore = awayScore
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        return match;
    }

    private static CompetitionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record SeededGroup(
        long EditionId,
        long DisciplineId,
        long PhaseId,
        long GroupId,
        IReadOnlyList<long> TeamIds);
}
