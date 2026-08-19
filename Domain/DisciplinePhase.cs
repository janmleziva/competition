namespace Competition.Domain;

public sealed class DisciplinePhase
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public required string Name { get; set; }
    public PhaseType Type { get; set; }
    public int Order { get; set; }
    public int PointsForWin { get; set; } = 2;
    public int PointsForDraw { get; set; } = 1;
    public int PointsForLoss { get; set; }
    public SetRuleType? SetRule { get; set; }
    public int? SetCount { get; set; }

    public ICollection<PhaseGroup> Groups { get; } = [];
    public ICollection<Match> Matches { get; } = [];
}
