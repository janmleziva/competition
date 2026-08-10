namespace Competition.Domain;

public sealed class PhaseGroup
{
    public long Id { get; set; }
    public long DisciplinePhaseId { get; set; }
    public DisciplinePhase DisciplinePhase { get; set; } = null!;
    public required string Name { get; set; }
    public int Order { get; set; }
    public int? Capacity { get; set; }

    public ICollection<PhaseGroupTeam> Teams { get; } = [];
    public ICollection<Match> Matches { get; } = [];
}
