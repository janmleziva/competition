namespace Competition.Domain;

public sealed class DisciplineTeamMember
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public long DisciplineTeamId { get; set; }
    public DisciplineTeam DisciplineTeam { get; set; } = null!;
    public long CompetitionEntryId { get; set; }
    public CompetitionEntry CompetitionEntry { get; set; } = null!;
    public int Order { get; set; }
}
