namespace Competition.Domain;

public sealed class RankingPointRule
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public int Rank { get; set; }
    public int Points { get; set; }
}
