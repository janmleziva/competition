namespace Competition.Domain;

public sealed class Competitor
{
    public long Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    public ICollection<CompetitionEntry> CompetitionEntries { get; } = [];
}
