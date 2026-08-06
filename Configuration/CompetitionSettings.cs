namespace Competition.Configuration;

public sealed class CompetitionSettings
{
    public const string SectionName = "Competition";

    public string Title { get; set; } = "Competition Countdown";

    public DateTimeOffset TargetDate { get; set; } = new(2026, 8, 22, 9, 0, 0, TimeSpan.FromHours(2));
}
