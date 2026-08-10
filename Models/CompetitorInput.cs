using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Competition.Models;

public sealed class CompetitorInput
{
    [Required(ErrorMessage = "Zadejte jméno.")]
    [StringLength(100, ErrorMessage = "Jméno může mít nejvýše 100 znaků.")]
    [Display(Name = "Jméno")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Zadejte příjmení.")]
    [StringLength(100, ErrorMessage = "Příjmení může mít nejvýše 100 znaků.")]
    [Display(Name = "Příjmení")]
    public string LastName { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}", ApplyFormatInEditMode = true)]
    [Display(Name = "Datum narození")]
    [ModelBinder(BinderType = typeof(DateOnlyInputModelBinder))]
    public DateOnly? DateOfBirth { get; set; }
}
