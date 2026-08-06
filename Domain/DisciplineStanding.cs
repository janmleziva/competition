namespace Competition.Domain;

public sealed class DisciplineStanding
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public long DisciplineTeamId { get; set; }
    public DisciplineTeam DisciplineTeam { get; set; } = null!;
    public int Rank { get; set; }
    public int PointsAwarded { get; set; }
}
