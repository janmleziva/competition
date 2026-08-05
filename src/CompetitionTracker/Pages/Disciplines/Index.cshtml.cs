using CompetitionTracker.Data;
using CompetitionTracker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Pages.Disciplines;

public class IndexModel(CompetitionDbContext db) : PageModel
{
    public IReadOnlyList<Discipline> Disciplines { get; private set; } = [];

    [BindProperty]
    public Discipline Input { get; set; } = new();

    public async Task OnGetAsync() => Disciplines = await db.Disciplines.AsNoTracking().OrderBy(d => d.Name).ToListAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await OnGetAsync(); return Page(); }
        db.Disciplines.Add(Input);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }
}
