using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Competition.Models;

public sealed class DateOnlyInputModelBinder : IModelBinder
{
    private static readonly string[] SupportedFormats =
    [
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "yyyy-MM-dd"
    ];

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var value = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (DateOnly.TryParseExact(
                value,
                SupportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            bindingContext.Result = ModelBindingResult.Success(date);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            "Zadejte datum ve formátu dd/mm/rrrr.");
        return Task.CompletedTask;
    }
}
