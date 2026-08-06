namespace Competition.Domain;

public sealed class CompetitionEntry
{
    public long Id { get; set; }
    public long CompetitionEditionId { get; set; }
    public CompetitionEdition CompetitionEdition { get; set; } = null!;
    public long CompetitorId { get; set; }
    public Competitor Competitor { get; set; } = null!;
    public int Seed { get; set; }

    public ICollection<DisciplineTeamMember> TeamMemberships { get; } = [];
}
