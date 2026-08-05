using System.ComponentModel.DataAnnotations;

namespace CompetitionTracker.Models;

public class Match
{
    public int Id { get; set; }

    [Required]
    public int DisciplineId { get; set; }

    public Discipline? Discipline { get; set; }

    [Required, StringLength(120)]
    public string RoundName { get; set; } = "Round 1";

    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsComplete { get; set; }

    public ICollection<MatchResult> Results { get; set; } = new List<MatchResult>();
}
