namespace Competition.Domain;

public sealed class DisciplineBonusPointRule
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public BonusPointType Type { get; set; }
    public int Points { get; set; } = 1;
}
