namespace Competition.Domain;

public sealed class RankingPointRule
{
    public long Id { get; set; }
    public long AwardPointSystemId { get; set; }
    public AwardPointSystem AwardPointSystem { get; set; } = null!;
    public int Rank { get; set; }
    public int Points { get; set; }
}
