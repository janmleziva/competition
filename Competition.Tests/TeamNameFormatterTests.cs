using Competition.Domain;
using Competition.Services;

namespace Competition.Tests;

public sealed class TeamNameFormatterTests
{
    [Fact]
    public void Format_UsesOnlySurnameWhenSurnameIsUnique()
    {
        var jan = Entry(1, "Jan", "Novák");
        var petr = Entry(2, "Petr", "Svoboda");
        var team = Team((jan, 1), (petr, 2));

        var labels = TeamNameFormatter.CreateEntryLabels([jan, petr]);

        Assert.Equal("Novák/Svoboda", TeamNameFormatter.Format(team, labels));
    }

    [Fact]
    public void Format_KeepsOneLetterWhenDuplicateSurnamesHaveDifferentInitials()
    {
        var petr = Entry(1, "Petr", "Marek");
        var david = Entry(2, "David", "Marek");
        var team = Team((petr, 1), (david, 2));

        var labels = TeamNameFormatter.CreateEntryLabels([petr, david]);

        Assert.Equal("Marek P./Marek D.", TeamNameFormatter.Format(team, labels));
    }

    [Fact]
    public void Format_UsesShortestUnambiguousFirstNamePrefixWhenInitialsCollide()
    {
        var robert = Entry(1, "Robert", "Hynek");
        var richard = Entry(2, "Richard", "Hynek");
        var team = Team((robert, 1), (richard, 2));

        var labels = TeamNameFormatter.CreateEntryLabels([robert, richard]);

        Assert.Equal("Hynek Ro./Hynek Ri.", TeamNameFormatter.Format(team, labels));
    }

    private static CompetitionEntry Entry(long id, string firstName, string lastName) => new()
    {
        Id = id,
        Competitor = new Competitor { FirstName = firstName, LastName = lastName }
    };

    private static DisciplineTeam Team(params (CompetitionEntry Entry, int Order)[] entries)
    {
        var team = new DisciplineTeam();
        foreach (var (entry, order) in entries)
        {
            team.Members.Add(new DisciplineTeamMember
            {
                CompetitionEntryId = entry.Id,
                CompetitionEntry = entry,
                DisciplineTeam = team,
                Order = order
            });
        }
        return team;
    }
}
