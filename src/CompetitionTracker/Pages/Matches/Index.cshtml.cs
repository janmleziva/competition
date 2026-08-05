using CompetitionTracker.Data;
using CompetitionTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Pages.Matches;

public class IndexModel(CompetitionDbContext db) : PageModel
{
    public IReadOnlyList<Match> Matches { get; private set; } = [];
    public SelectList DisciplineOptions { get; private set; } = new(Array.Empty<Discipline>(), "Id", "Name");

    [BindProperty]
    public Match Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        db.Matches.Add(Input);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var disciplines = await db.Disciplines.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        DisciplineOptions = new SelectList(disciplines, "Id", "Name");
        Matches = await db.Matches.AsNoTracking().Include(m => m.Discipline).OrderByDescending(m => m.ScheduledAt).ToListAsync();
    }
}
