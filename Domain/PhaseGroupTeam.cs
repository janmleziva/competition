namespace Competition.Domain;

public sealed class PhaseGroupTeam
{
    public long Id { get; set; }
    public long DisciplinePhaseId { get; set; }
    public long PhaseGroupId { get; set; }
    public PhaseGroup PhaseGroup { get; set; } = null!;
    public long DisciplineTeamId { get; set; }
    public DisciplineTeam DisciplineTeam { get; set; } = null!;
    public int Seed { get; set; }
}
