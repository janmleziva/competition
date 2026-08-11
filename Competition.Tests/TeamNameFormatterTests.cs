using Competition.Domain;
using Competition.Services;

namespace Competition.Tests;

public sealed class TeamNameFormatterTests
{
    [Fact]
    public void Format_UsesSurnameAndShortestUnambiguousFirstNamePrefix()
    {
        var jan = Entry(1, "Jan", "Novák");
        var jana = Entry(2, "Jana", "Novák");
        var petr = Entry(3, "Petr", "Svoboda");
        var team = Team((jan, 1), (jana, 2), (petr, 3));

        var labels = TeamNameFormatter.CreateEntryLabels([jan, jana, petr]);

        Assert.Equal("Novák Jan./Novák Jana./Svoboda P.", TeamNameFormatter.Format(team, labels));
    }

    [Fact]
    public void Format_KeepsOneLetterWhenDuplicateSurnamesHaveDifferentInitials()
    {
        var jan = Entry(1, "Jan", "Novák");
        var petr = Entry(2, "Petr", "Novák");
        var team = Team((jan, 1), (petr, 2));

        var labels = TeamNameFormatter.CreateEntryLabels([jan, petr]);

        Assert.Equal("Novák J./Novák P.", TeamNameFormatter.Format(team, labels));
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
