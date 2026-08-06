using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Competition.Models;

public sealed class EditionInput : IValidatableObject
{
    [Required(ErrorMessage = "Zadejte název soutěže.")]
    [StringLength(200, ErrorMessage = "Název soutěže může mít nejvýše 200 znaků.")]
    [Display(Name = "Název soutěže")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Zadejte město.")]
    [StringLength(120, ErrorMessage = "Město může mít nejvýše 120 znaků.")]
    [Display(Name = "Město")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vyberte datum začátku.")]
    [DataType(DataType.Date)]
    [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}", ApplyFormatInEditMode = true)]
    [Display(Name = "Začátek")]
    [ModelBinder(BinderType = typeof(DateOnlyInputModelBinder))]
    public DateOnly? StartDate { get; set; }

    [Required(ErrorMessage = "Vyberte datum konce.")]
    [DataType(DataType.Date)]
    [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}", ApplyFormatInEditMode = true)]
    [Display(Name = "Konec")]
    [ModelBinder(BinderType = typeof(DateOnlyInputModelBinder))]
    public DateOnly? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate is not null && EndDate is not null && EndDate < StartDate)
        {
            yield return new ValidationResult(
                "Datum konce musí být stejné nebo pozdější než datum začátku.",
                [nameof(EndDate)]);
        }
    }
}
