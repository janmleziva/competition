namespace Competition.Domain;

public sealed class DisciplineBonusAward
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public long DisciplineTeamId { get; set; }
    public DisciplineTeam DisciplineTeam { get; set; } = null!;
    public BonusPointType Type { get; set; }
    public int PointsAwarded { get; set; }
    public int MetricTotal { get; set; }
    public int MatchCount { get; set; }
}
