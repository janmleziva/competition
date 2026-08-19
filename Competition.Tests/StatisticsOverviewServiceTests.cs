using Competition.Data;
using Competition.Domain;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class StatisticsOverviewServiceTests
{
    [Fact]
    public async Task Individuals_ProvideIndependentPlacementAndPointRankings()
    {
        await using var db = CreateDbContext();
        var alice = new Competitor { FirstName = "Alice", LastName = "První" };
        var bob = new Competitor { FirstName = "Bob", LastName = "Bodový" };
        var tennis = new Discipline { Name = "Tenis" };
        var edition2025 = CreateEdition("Cup 2025", 2025);
        var edition2026 = CreateEdition("Cup 2026", 2026);

        AddResult(edition2025, tennis, alice, 1, 2);
        AddResult(edition2025, tennis, bob, 2, 10);
        AddResult(edition2026, tennis, alice, 1, 2);
        AddResult(edition2026, tennis, bob, 2, 10, bonusPoints: 3);
        db.AddRange(edition2025, edition2026);
        await db.SaveChangesAsync();

        var result = await new StatisticsOverviewService(db).GetIndividualsAsync();

        Assert.Equal(new[] { alice.Id, bob.Id }, result.ByPlacements.Select(row => row.CompetitorId));
        Assert.Equal(new[] { bob.Id, alice.Id }, result.ByPoints.Select(row => row.CompetitorId));
        Assert.Equal(23, result.ByPoints[0].TotalPoints);
        Assert.Equal("1. místo 2x", result.ByPlacements[0].Placements.Format());
        Assert.All(result.ByPlacements, row => Assert.Equal(2, row.EditionCount));
    }

    [Fact]
    public async Task Edition_FiltersResultsAndReturnsNullForUnknownEdition()
    {
        await using var db = CreateDbContext();
        var competitor = new Competitor { FirstName = "Eva", LastName = "Ročníková" };
        var tennis = new Discipline { Name = "Tenis" };
        var edition2025 = CreateEdition("Cup 2025", 2025);
        var edition2026 = CreateEdition("Cup 2026", 2026);
        AddResult(edition2025, tennis, competitor, 2, 4);
        AddResult(edition2026, tennis, competitor, 1, 8);
        db.AddRange(edition2025, edition2026);
        await db.SaveChangesAsync();

        var service = new StatisticsOverviewService(db);
        var result = await service.GetEditionAsync(edition2025.Id);

        Assert.NotNull(result);
        Assert.Equal("Cup 2025", result.EditionName);
        Assert.Single(result.ByPlacements);
        Assert.Equal("2. místo 1x", result.ByPlacements[0].Placements.Format());
        Assert.Equal(4, result.ByPlacements[0].TotalPoints);
        Assert.Null(await service.GetEditionAsync(999));
    }

    [Fact]
    public async Task Discipline_BuildsLeaderboardAndRecordHolders()
    {
        await using var db = CreateDbContext();
        var alice = new Competitor { FirstName = "Alice", LastName = "První" };
        var bob = new Competitor { FirstName = "Bob", LastName = "Druhý" };
        var tennis = new Discipline { Name = "Tenis" };
        var padel = new Discipline { Name = "Padel" };
        var edition2025 = CreateEdition("Cup 2025", 2025);
        var edition2026 = CreateEdition("Cup 2026", 2026);
        AddResult(edition2025, tennis, alice, 1, 5);
        AddResult(edition2025, tennis, bob, 2, 4);
        AddResult(edition2026, tennis, alice, 1, 5);
        AddResult(edition2026, tennis, bob, 3, 3);
        AddResult(edition2026, padel, bob, 1, 8);
        db.AddRange(edition2025, edition2026);
        await db.SaveChangesAsync();

        var result = await new StatisticsOverviewService(db).GetDisciplinesAsync(tennis.Id);

        Assert.Equal("Tenis", result.SelectedDisciplineName);
        Assert.Equal(new[] { alice.Id, bob.Id }, result.Rankings.Select(row => row.CompetitorId));
        var wins = Assert.Single(result.Records, record => record.Label == "Nejvíce vítězství");
        Assert.Equal(2, wins.Value);
        Assert.Equal(alice.Id, Assert.Single(wins.Holders).CompetitorId);
        var starts = Assert.Single(result.Records, record => record.Label == "Nejvíce účastí");
        Assert.Equal(2, starts.Value);
        Assert.Equal(2, starts.Holders.Count);
    }

    [Fact]
    public async Task EditionCompetitorResults_GroupMatchesAndOrientScoresToParticipant()
    {
        await using var db = CreateDbContext();
        var participant = new Competitor { FirstName = "Jan", LastName = "Hráč" };
        var teammate = new Competitor { FirstName = "Tomáš", LastName = "Parťák" };
        var opponent = new Competitor { FirstName = "Petr", LastName = "Soupeř" };
        var edition = CreateEdition("Cup 2026", 2026);
        var participantEntry = AddEntry(edition, participant, 1);
        var teammateEntry = AddEntry(edition, teammate, 2);
        var opponentEntry = AddEntry(edition, opponent, 3);
        var catalogDiscipline = new Discipline { Name = "Beach" };
        var discipline = new CompetitionDiscipline
        {
            CompetitionEdition = edition,
            Discipline = catalogDiscipline,
            PlayingSystem = PlayingSystemType.RoundRobin,
            TeamSize = 2,
            Order = 1,
            UsesSetScores = true
        };
        edition.Disciplines.Add(discipline);
        var participantTeam = AddTeam(discipline, 1, participantEntry, teammateEntry);
        var opponentTeam = AddTeam(discipline, 2, opponentEntry);
        var phase = new DisciplinePhase
        {
            CompetitionDiscipline = discipline,
            Name = "Skupina",
            Type = PhaseType.Group,
            Order = 1
        };
        discipline.Phases.Add(phase);
        var group = new PhaseGroup { DisciplinePhase = phase, Name = "Skupina A", Order = 1 };
        phase.Groups.Add(group);
        var match = new Match
        {
            DisciplinePhase = phase,
            PhaseGroup = group,
            HomeTeam = opponentTeam,
            AwayTeam = participantTeam,
            Name = "1. kolo",
            Order = 1,
            Status = MatchStatus.Completed,
            HomeScore = 1,
            AwayScore = 2
        };
        match.SetScores.Add(new MatchSetScore { Match = match, SetNumber = 1, HomeScore = 21, AwayScore = 18 });
        match.SetScores.Add(new MatchSetScore { Match = match, SetNumber = 2, HomeScore = 15, AwayScore = 21 });
        phase.Matches.Add(match);
        db.Add(edition);
        await db.SaveChangesAsync();

        var service = new StatisticsOverviewService(db);
        var result = await service.GetEditionCompetitorResultsAsync(edition.Id, participant.Id);

        Assert.NotNull(result);
        Assert.Equal("Cup 2026", result.EditionName);
        var disciplineResult = Assert.Single(result.Disciplines);
        Assert.Equal("Beach", disciplineResult.DisciplineName);
        var matchResult = Assert.Single(disciplineResult.Matches);
        Assert.Equal("Skupina", matchResult.PhaseName);
        Assert.Equal("Skupina A", matchResult.GroupName);
        Assert.Equal("Skupina A", matchResult.DisplayName);
        Assert.Equal("1. kolo", matchResult.StageLabel);
        Assert.Equal("Tomáš Parťák", matchResult.Teammates);
        Assert.Equal("Petr Soupeř", matchResult.Opponent);
        Assert.Equal("Výhra", matchResult.Outcome);
        Assert.Equal((2, 1), (matchResult.ScoreFor, matchResult.ScoreAgainst));
        Assert.Equal(new[] { "18:21", "21:15" }, matchResult.Subscores);
        Assert.Null(await service.GetEditionCompetitorResultsAsync(edition.Id, 999));
    }

    [Fact]
    public async Task DisciplineTeamResults_ListAllMatchesAndOrientResultsToTeam()
    {
        await using var db = CreateDbContext();
        var participant = new Competitor { FirstName = "Jan", LastName = "Hráč" };
        var teammate = new Competitor { FirstName = "Tomáš", LastName = "Parťák" };
        var opponent = new Competitor { FirstName = "Petr", LastName = "Soupeř" };
        var edition = CreateEdition("Cup 2026", 2026);
        var participantEntry = AddEntry(edition, participant, 1);
        var teammateEntry = AddEntry(edition, teammate, 2);
        var opponentEntry = AddEntry(edition, opponent, 3);
        var discipline = new CompetitionDiscipline
        {
            CompetitionEdition = edition,
            Discipline = new Discipline { Name = "Beach" },
            PlayingSystem = PlayingSystemType.RoundRobin,
            TeamSize = 2,
            Order = 1,
            UsesSetScores = true
        };
        edition.Disciplines.Add(discipline);
        var participantTeam = AddTeam(discipline, 1, participantEntry, teammateEntry);
        var opponentTeam = AddTeam(discipline, 2, opponentEntry);
        var phase = new DisciplinePhase
        {
            CompetitionDiscipline = discipline,
            Name = "Skupina",
            Type = PhaseType.Group,
            Order = 1,
            SetRule = SetRuleType.FixedSets,
            SetCount = 2
        };
        discipline.Phases.Add(phase);
        var group = new PhaseGroup { DisciplinePhase = phase, Name = "Skupina A", Order = 1 };
        phase.Groups.Add(group);
        var completedMatch = new Match
        {
            DisciplinePhase = phase,
            PhaseGroup = group,
            HomeTeam = opponentTeam,
            AwayTeam = participantTeam,
            Name = "1. kolo",
            Order = 1,
            Status = MatchStatus.Completed,
            HomeScore = 1,
            AwayScore = 1
        };
        completedMatch.SetScores.Add(new MatchSetScore
        {
            Match = completedMatch,
            SetNumber = 1,
            HomeScore = 10,
            AwayScore = 7
        });
        completedMatch.SetScores.Add(new MatchSetScore
        {
            Match = completedMatch,
            SetNumber = 2,
            HomeScore = 5,
            AwayScore = 10
        });
        phase.Matches.Add(completedMatch);
        phase.Matches.Add(new Match
        {
            DisciplinePhase = phase,
            PhaseGroup = group,
            HomeTeam = participantTeam,
            AwayTeam = opponentTeam,
            Name = "2. kolo",
            Order = 2,
            Status = MatchStatus.Scheduled
        });
        db.Add(edition);
        await db.SaveChangesAsync();

        var service = new StatisticsOverviewService(db);
        var result = await service.GetDisciplineTeamResultsAsync(
            edition.Id, discipline.Id, participantTeam.Id);

        Assert.NotNull(result);
        Assert.Equal("Cup 2026", result.EditionName);
        Assert.Equal("Beach", result.DisciplineName);
        Assert.Equal("Hráč/Parťák", result.TeamName);
        Assert.Equal(2, result.Matches.Count);
        var completed = result.Matches[0];
        Assert.Equal(opponentTeam.Id, completed.OpponentTeamId);
        Assert.Equal("Soupeř", completed.OpponentTeamName);
        Assert.Equal("Výhra", completed.Outcome);
        Assert.Equal((1, 1), (completed.ScoreFor, completed.ScoreAgainst));
        Assert.Equal(new[] { "7:10", "10:5" }, completed.Subscores);
        Assert.Equal("Neodehráno", result.Matches[1].Outcome);
        Assert.Null(result.Matches[1].ScoreFor);
        Assert.Null(await service.GetDisciplineTeamResultsAsync(edition.Id, discipline.Id, 999));
    }

    private static CompetitionEdition CreateEdition(string name, int year) => new()
    {
        Name = name,
        City = "Praha",
        StartDate = new DateOnly(year, 8, 1),
        EndDate = new DateOnly(year, 8, 2),
        CreationToken = Guid.NewGuid()
    };

    private static CompetitionEntry AddEntry(CompetitionEdition edition, Competitor competitor, int seed)
    {
        var entry = new CompetitionEntry
        {
            CompetitionEdition = edition,
            Competitor = competitor,
            Seed = seed
        };
        edition.Entries.Add(entry);
        return entry;
    }

    private static DisciplineTeam AddTeam(
        CompetitionDiscipline discipline,
        int seed,
        params CompetitionEntry[] members)
    {
        var team = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = seed };
        foreach (var (member, index) in members.Select((member, index) => (member, index)))
        {
            team.Members.Add(new DisciplineTeamMember
            {
                CompetitionDisciplineId = discipline.Id,
                CompetitionEntry = member,
                Order = index + 1
            });
        }

        discipline.Teams.Add(team);
        return team;
    }

    private static void AddResult(
        CompetitionEdition edition,
        Discipline catalogDiscipline,
        Competitor competitor,
        int rank,
        int points,
        int bonusPoints = 0)
    {
        var entry = edition.Entries.SingleOrDefault(item => item.Competitor == competitor);
        if (entry is null)
        {
            entry = new CompetitionEntry
            {
                CompetitionEdition = edition,
                Competitor = competitor,
                Seed = edition.Entries.Count + 1
            };
            edition.Entries.Add(entry);
        }

        var discipline = edition.Disciplines.SingleOrDefault(item => item.Discipline == catalogDiscipline);
        if (discipline is null)
        {
            discipline = new CompetitionDiscipline
            {
                CompetitionEdition = edition,
                Discipline = catalogDiscipline,
                PlayingSystem = PlayingSystemType.RoundRobin,
                TeamSize = 1,
                Order = edition.Disciplines.Count + 1,
                IsClosed = true
            };
            edition.Disciplines.Add(discipline);
        }

        var team = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = discipline.Teams.Count + 1 };
        team.Members.Add(new DisciplineTeamMember
        {
            CompetitionDisciplineId = discipline.Id,
            CompetitionEntry = entry,
            Order = 1
        });
        discipline.Teams.Add(team);
        discipline.FinalStandings.Add(new DisciplineStanding
        {
            CompetitionDiscipline = discipline,
            DisciplineTeam = team,
            Rank = rank,
            PointsAwarded = points
        });
        if (bonusPoints > 0)
        {
            team.BonusAwards.Add(new DisciplineBonusAward
            {
                CompetitionDiscipline = discipline,
                DisciplineTeam = team,
                Type = BonusPointType.HighestAverageScoreFor,
                PointsAwarded = bonusPoints,
                MetricTotal = 10,
                MatchCount = 1
            });
        }
    }

    private static CompetitionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
