using CompetitionTracker.Data;
using CompetitionTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Pages.Competitors;

public class IndexModel(CompetitionDbContext db) : PageModel
{
    public IReadOnlyList<Competitor> Competitors { get; private set; } = [];

    [BindProperty]
    public Competitor Input { get; set; } = new();

    public async Task OnGetAsync() => Competitors = await db.Competitors.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await OnGetAsync(); return Page(); }
        db.Competitors.Add(Input);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }
}
