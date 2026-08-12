using System.ComponentModel.DataAnnotations;
using Competition.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Competition.Models;

public sealed class DisciplineCatalogInput
{
    [Required(ErrorMessage = "Zadejte název disciplíny.")]
    [StringLength(120, ErrorMessage = "Název může mít nejvýše 120 znaků.")]
    public string Name { get; set; } = string.Empty;
}

public sealed class EditionDisciplineInput : IValidatableObject
{
    [Range(1, long.MaxValue, ErrorMessage = "Vyberte disciplínu.")]
    public long DisciplineId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pořadí musí být kladné číslo.")]
    public int Order { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "Velikost týmu musí být mezi 1 a 100.")]
    public int TeamSize { get; set; } = 1;

    public PlayingSystemType PlayingSystem { get; set; } = PlayingSystemType.RoundRobin;

    public bool UsesSetScores { get; set; }

    public long? AwardPointSystemId { get; set; }

    [Range(1, 10, ErrorMessage = "Počet vítězných setů musí být mezi 1 a 10.")]
    public int? SetsToWin { get; set; }

    [StringLength(2000, ErrorMessage = "Informace o disciplíně mohou mít nejvýše 2000 znaků.")]
    public string? Description { get; set; }

    [ModelBinder(BinderType = typeof(DateTimeInputModelBinder))]
    public DateTime? ScheduledAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UsesSetScores && SetsToWin is null)
        {
            yield return new ValidationResult(
                "U disciplíny se sety zadejte počet vítězných setů.",
                [nameof(SetsToWin)]);
        }
    }
}
