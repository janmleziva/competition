using System.ComponentModel.DataAnnotations;

namespace Competition.Models;

public sealed class AwardPointSystemInput
{
    [Required(ErrorMessage = "Zadejte název bodovacího systému.")]
    [StringLength(120, ErrorMessage = "Název může mít nejvýše 120 znaků.")]
    public string Name { get; set; } = string.Empty;

    public List<RankingPointRuleInput> Rules { get; set; } = [];
}

public sealed class RankingPointRuleInput
{
    [Range(1, 1000, ErrorMessage = "Pořadí musí být kladné číslo.")]
    public int Rank { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Počet bodů nesmí být záporný.")]
    public int Points { get; set; }
}
