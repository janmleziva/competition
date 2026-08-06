namespace Competition.Domain;

public sealed class CompetitionEdition
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public ICollection<CompetitionEntry> Entries { get; } = [];
    public ICollection<CompetitionDiscipline> Disciplines { get; } = [];
}
