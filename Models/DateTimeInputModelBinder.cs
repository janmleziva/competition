using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Competition.Models;

public sealed class DateTimeInputModelBinder : IModelBinder
{
    private static readonly string[] SupportedFormats =
    [
        "dd/MM/yyyy HH:mm",
        "d/M/yyyy H:mm",
        "dd.MM.yyyy HH:mm",
        "d.M.yyyy H:mm",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm"
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

        if (DateTime.TryParseExact(
                value,
                SupportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            bindingContext.Result = ModelBindingResult.Success(dateTime);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            "Zadejte datum a čas ve formátu dd/mm/rrrr hh:mm.");
        return Task.CompletedTask;
    }
}
