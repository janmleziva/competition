namespace Competition.Domain;

public sealed class AwardPointSystem
{
    public long Id { get; set; }
    public required string Name { get; set; }

    public ICollection<RankingPointRule> Rules { get; } = [];
    public ICollection<CompetitionDiscipline> CompetitionDisciplines { get; } = [];
}
