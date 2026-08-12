using Competition.Data;
using Competition.Domain;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class CompetitorStatisticsServiceTests
{
    [Fact]
    public async Task Statistics_AggregateEditionsDisciplinesAndTeamMembers()
    {
        await using var db = CreateDbContext();
        var target = new Competitor { FirstName = "Jan", LastName = "Novák" };
        var alice = new Competitor { FirstName = "Alice", LastName = "Malá" };
        var bob = new Competitor { FirstName = "Bob", LastName = "Velký" };
        var padel = new Discipline { Name = "Padel" };
        var tennis = new Discipline { Name = "Tenis" };

        var edition2025 = CreateEdition("Cup 2025", new DateOnly(2025, 8, 1));
        var target2025 = AddEntry(edition2025, target, 1);
        var alice2025 = AddEntry(edition2025, alice, 2);
        var bob2025 = AddEntry(edition2025, bob, 3);
        AddResult(edition2025, padel, 1, 2, 3, target2025, bob2025);
        AddResult(edition2025, tennis, 2, 1, 4, target2025);

        var edition2024 = CreateEdition("Cup 2024", new DateOnly(2024, 8, 1));
        var target2024 = AddEntry(edition2024, target, 1);
        var alice2024 = AddEntry(edition2024, alice, 2);
        AddEntry(edition2024, bob, 3);
        AddResult(edition2024, padel, 1, 1, 5, target2024, alice2024);
        AddResult(edition2024, tennis, 2, 2, 2, target2024);

        db.AddRange(edition2025, edition2024);
        await db.SaveChangesAsync();

        var scoring = new CompetitionScoringService(
            db,
            new GroupStandingsService(db),
            new AwardPointSystemService(db));
        var statistics = await new CompetitorStatisticsService(db, scoring).GetAsync(target.Id);

        Assert.NotNull(statistics);
        Assert.Equal(new[] { "Cup 2024", "Cup 2025" }, statistics.Editions.Select(row => row.EditionName));
        Assert.Equal(new[] { 7, 7 }, statistics.Editions.Select(row => row.TotalPoints));
        Assert.All(statistics.Editions, row => Assert.Equal(1, row.OverallPlace));
        Assert.Equal(14, statistics.TotalPoints);
        Assert.Equal(8, statistics.TotalsByDiscipline["Padel"]);
        Assert.Equal(6, statistics.TotalsByDiscipline["Tenis"]);
        Assert.Equal(new[] { "Padel", "Tenis" }, statistics.ByDiscipline.Select(row => row.DisciplineName));

        Assert.Collection(statistics.ByTeamMember,
            teammate =>
            {
                Assert.Equal(alice.Id, teammate.CompetitorId);
                Assert.Equal(5, teammate.TotalPoints);
                Assert.Equal(5, teammate.Disciplines["Padel"]);
            },
            teammate =>
            {
                Assert.Equal(bob.Id, teammate.CompetitorId);
                Assert.Equal(3, teammate.TotalPoints);
                Assert.Equal(3, teammate.Disciplines["Padel"]);
            });
    }

    [Fact]
    public async Task Statistics_ReturnNullForUnknownCompetitor()
    {
        await using var db = CreateDbContext();
        var scoring = new CompetitionScoringService(
            db,
            new GroupStandingsService(db),
            new AwardPointSystemService(db));

        Assert.Null(await new CompetitorStatisticsService(db, scoring).GetAsync(999));
    }

    private static CompetitionEdition CreateEdition(string name, DateOnly startDate) => new()
    {
        Name = name,
        City = "Praha",
        StartDate = startDate,
        EndDate = startDate.AddDays(1),
        CreationToken = Guid.NewGuid()
    };

    private static CompetitionEntry AddEntry(
        CompetitionEdition edition,
        Competitor competitor,
        int seed)
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

    private static void AddResult(
        CompetitionEdition edition,
        Discipline catalogDiscipline,
        int order,
        int rank,
        int points,
        params CompetitionEntry[] members)
    {
        var discipline = new CompetitionDiscipline
        {
            CompetitionEdition = edition,
            Discipline = catalogDiscipline,
            PlayingSystem = PlayingSystemType.RoundRobin,
            TeamSize = members.Length,
            Order = order,
            IsClosed = true
        };
        var team = new DisciplineTeam { CompetitionDiscipline = discipline, Seed = 1 };
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
        discipline.FinalStandings.Add(new DisciplineStanding
        {
            CompetitionDiscipline = discipline,
            DisciplineTeam = team,
            Rank = rank,
            PointsAwarded = points
        });
        edition.Disciplines.Add(discipline);
    }

    private static CompetitionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
