namespace Competition.Domain;

public sealed class DisciplineParticipantAssignment
{
    public long Id { get; set; }
    public long CompetitionDisciplineId { get; set; }
    public CompetitionDiscipline CompetitionDiscipline { get; set; } = null!;
    public long CompetitionEntryId { get; set; }
    public CompetitionEntry CompetitionEntry { get; set; } = null!;
}
