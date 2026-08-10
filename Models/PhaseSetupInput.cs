using System.ComponentModel.DataAnnotations;
using Competition.Domain;

namespace Competition.Models;

public sealed class PhaseInput
{
    [Required(ErrorMessage = "Zadejte název fáze.")]
    [StringLength(120, ErrorMessage = "Název fáze může mít nejvýše 120 znaků.")]
    public string Name { get; set; } = string.Empty;

    public PhaseType Type { get; set; } = PhaseType.Group;

    [Range(1, int.MaxValue, ErrorMessage = "Pořadí musí být kladné číslo.")]
    public int Order { get; set; }

    [Range(0, int.MaxValue)]
    public int PointsForWin { get; set; } = 2;

    [Range(0, int.MaxValue)]
    public int PointsForDraw { get; set; } = 1;

    [Range(0, int.MaxValue)]
    public int PointsForLoss { get; set; }
}

public sealed class PhaseGroupInput
{
    [Required(ErrorMessage = "Zadejte název skupiny.")]
    [StringLength(80, ErrorMessage = "Název skupiny může mít nejvýše 80 znaků.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Pořadí musí být kladné číslo.")]
    public int Order { get; set; } = 1;

    [Range(2, int.MaxValue, ErrorMessage = "Počet týmů musí být alespoň 2.")]
    public int? Capacity { get; set; }
}

public sealed class RandomGroupAssignmentInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Počet týmů musí být kladný.")]
    public int TeamCount { get; set; } = 1;
}

public sealed class MatchSlotInput
{
    [Required(ErrorMessage = "Zadejte název zápasu.")]
    [StringLength(120, ErrorMessage = "Název zápasu může mít nejvýše 120 znaků.")]
    public string Name { get; set; } = string.Empty;

    public long? HomeTeamId { get; set; }
    public long? AwayTeamId { get; set; }
}

public sealed class MatchResultInput
{
    [Range(1, long.MaxValue)]
    public long MatchId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Skóre nesmí být záporné.")]
    public int? HomeScore { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Skóre nesmí být záporné.")]
    public int? AwayScore { get; set; }

    [Range(0, int.MaxValue)]
    public int Version { get; set; }
}

public sealed class MatchTeamsInput
{
    [Range(1, long.MaxValue)]
    public long MatchId { get; set; }

    public string? HomeSelection { get; set; }
    public string? AwaySelection { get; set; }

    public long? HomeTeamId { get; set; }
    public long? AwayTeamId { get; set; }
}

public sealed class MatchSetScoresInput
{
    [Range(1, long.MaxValue)]
    public long MatchId { get; set; }

    [Range(0, int.MaxValue)]
    public int Version { get; set; }

    public List<MatchSetScoreInput> Sets { get; set; } = [];
}

public sealed class MatchSetScoreInput
{
    [Range(1, 19)]
    public int SetNumber { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Skóre setu nesmí být záporné.")]
    public int? HomeScore { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Skóre setu nesmí být záporné.")]
    public int? AwayScore { get; set; }
}
