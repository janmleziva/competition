using System.ComponentModel.DataAnnotations;

namespace CompetitionTracker.Models;

public class Competitor
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? ClubOrTeam { get; set; }

    public ICollection<MatchResult> MatchResults { get; set; } = new List<MatchResult>();
}
