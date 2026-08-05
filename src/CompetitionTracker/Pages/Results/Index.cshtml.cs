using CompetitionTracker.Data;
using CompetitionTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Pages.Results;

public class IndexModel(CompetitionDbContext db) : PageModel
{
    public IReadOnlyList<MatchResult> Results { get; private set; } = [];
    public SelectList MatchOptions { get; private set; } = new(Array.Empty<SelectListItem>(), "Value", "Text");
    public SelectList CompetitorOptions { get; private set; } = new(Array.Empty<Competitor>(), "Id", "Name");

    [BindProperty]
    public MatchResult Input { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        db.MatchResults.Add(Input);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var matches = await db.Matches.AsNoTracking().Include(m => m.Discipline).OrderByDescending(m => m.ScheduledAt).ToListAsync();
        MatchOptions = new SelectList(matches.Select(m => new SelectListItem($"{m.Discipline?.Name} - {m.RoundName}", m.Id.ToString())), "Value", "Text");
        var competitors = await db.Competitors.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        CompetitorOptions = new SelectList(competitors, "Id", "Name");
        Results = await db.MatchResults.AsNoTracking().Include(r => r.Competitor).Include(r => r.Match)!.ThenInclude(m => m.Discipline).OrderByDescending(r => r.Match!.ScheduledAt).ToListAsync();
    }
}
