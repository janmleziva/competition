namespace Competition.Domain;

public sealed class DisciplineTeam
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public int Seed { get; set; }

    public ICollection<DisciplineTeamMember> Members { get; } = [];
    public ICollection<PhaseGroupTeam> GroupAssignments { get; } = [];
    public ICollection<DisciplineStanding> FinalStandingEntries { get; } = [];
}
