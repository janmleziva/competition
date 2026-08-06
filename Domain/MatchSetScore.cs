namespace Competition.Domain;

public sealed class MatchSetScore
{
    public long Id { get; set; }
    public long MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int SetNumber { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
}
