using System.ComponentModel.DataAnnotations;
using Competition.Domain;

namespace Competition.Models;

public sealed class BonusPointRuleInput
{
    public BonusPointType Type { get; set; }
    public bool Enabled { get; set; }

    [Range(0, 1000, ErrorMessage = "Počet bonusových bodů musí být mezi 0 a 1000.")]
    public int Points { get; set; } = 1;
}
