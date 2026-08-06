namespace Competition.Domain;

public sealed class Discipline
{
    public long Id { get; set; }
    public required string Name { get; set; }

    public ICollection<CompetitionDiscipline> CompetitionDisciplines { get; } = [];
}
