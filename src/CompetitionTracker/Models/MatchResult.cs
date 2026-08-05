using System.ComponentModel.DataAnnotations;

namespace CompetitionTracker.Models;

public class MatchResult
{
    public int Id { get; set; }

    [Required]
    public int MatchId { get; set; }

    public Match? Match { get; set; }

    [Required]
    public int CompetitorId { get; set; }

    public Competitor? Competitor { get; set; }

    public int? Rank { get; set; }

    [Range(0, 100000)]
    public decimal Points { get; set; }

    [StringLength(120)]
    public string? RawScore { get; set; }
}
