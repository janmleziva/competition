using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Competition.Models;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Editions;

public sealed class CompetitorsModel(ICompetitorAdministrationService competitors) : PageModel
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

    public EditionRegistrationDetails Registration { get; private set; } = null!;

    [BindProperty]
    public long CompetitorId { get; set; }

    [BindProperty]
    public int Seed { get; set; }

    public CompetitorInput NewCompetitor { get; private set; } = new();
    public bool ShowDuplicateConfirmation { get; private set; }
    public bool ExpandNewCompetitorForm { get; private set; }
    public string? NewCompetitorDetailsOpen => ExpandNewCompetitorForm ? "open" : null;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken) =>
        await LoadPageAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostRegisterAsync(long id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        if (Seed <= 0)
        {
            ModelState.AddModelError(nameof(Seed), "Nasazení musí být kladné celé číslo.");
        }

        if (!ModelState.IsValid)
        {
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        try
        {
            await competitors.RegisterAsync(id, CompetitorId, Seed, cancellationToken);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        StatusMessage = "Soutěžící byl přihlášen do ročníku.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCreateAndRegisterAsync(
        long id,
        CompetitorInput newCompetitor,
        bool allowDuplicateName,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        NewCompetitor = newCompetitor;
        if (!ModelState.IsValid)
        {
            ExpandNewCompetitorForm = true;
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        try
        {
            await competitors.CreateAndRegisterAsync(
                id,
                newCompetitor,
                allowDuplicateName,
                cancellationToken);
        }
        catch (DuplicateCompetitorException)
        {
            ShowDuplicateConfirmation = true;
            ExpandNewCompetitorForm = true;
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ExpandNewCompetitorForm = true;
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        StatusMessage = "Soutěžící byl vytvořen a přihlášen do ročníku.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUpdateSeedAsync(long id, long entryId, int entrySeed, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            var result = await competitors.UpdateSeedAsync(id, entryId, entrySeed, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            StatusMessage = result.WasSwapped
                ? $"Nasazení bylo prohozeno se soutěžícím {result.SwappedCompetitorName}."
                : "Nasazení bylo změněno.";
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(long id, long entryId, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        try
        {
            if (!await competitors.RemoveAsync(id, entryId, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        StatusMessage = "Soutěžící byl z ročníku odhlášen.";
        return RedirectToPage(new { id });
    }

    public string FormatDate(DateOnly? date) =>
        date?.ToString("d. MMMM yyyy", CzechCulture) ?? "Neuvedeno";

    private async Task<IActionResult> ReloadOrNotFoundAsync(long id, CancellationToken cancellationToken) =>
        await LoadPageAsync(id, cancellationToken) ? Page() : NotFound();

    private async Task<bool> LoadPageAsync(long id, CancellationToken cancellationToken)
    {
        var registration = await competitors.GetEditionRegistrationAsync(id, cancellationToken);
        if (registration is null)
        {
            return false;
        }

        Registration = registration;
        if (Seed <= 0)
        {
            Seed = registration.Entries.Count == 0 ? 1 : registration.Entries.Max(x => x.Seed) + 1;
        }
        return true;
    }
}
