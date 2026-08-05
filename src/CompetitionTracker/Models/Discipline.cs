using System.ComponentModel.DataAnnotations;

namespace CompetitionTracker.Models;

public class Discipline
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ScoringNotes { get; set; }

    public ICollection<Match> Matches { get; set; } = new List<Match>();
}
