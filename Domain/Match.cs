namespace Competition.Domain;

public sealed class Match
{
    public long Id { get; set; }
    public long DisciplinePhaseId { get; set; }
    public DisciplinePhase DisciplinePhase { get; set; } = null!;
    public long? PhaseGroupId { get; set; }
    public PhaseGroup? PhaseGroup { get; set; }
    public long? HomeTeamId { get; set; }
    public DisciplineTeam? HomeTeam { get; set; }
    public long? AwayTeamId { get; set; }
    public DisciplineTeam? AwayTeam { get; set; }
    public long? HomeSourceMatchId { get; set; }
    public long? AwaySourceMatchId { get; set; }
    public long? HomeSourceGroupId { get; set; }
    public int? HomeSourceRank { get; set; }
    public long? AwaySourceGroupId { get; set; }
    public int? AwaySourceRank { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
    public MatchStatus Status { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }

    public ICollection<MatchSetScore> SetScores { get; } = [];
}
