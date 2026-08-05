using CompetitionTracker.Data;
using CompetitionTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace CompetitionTracker.Services;

public class StandingsService(CompetitionDbContext db)
{
    public async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CancellationToken cancellationToken = default)
    {
        var disciplines = await db.Disciplines
            .AsNoTracking()
            .OrderBy(discipline => discipline.Name)
            .Select(discipline => discipline.Name)
            .ToListAsync(cancellationToken);

        var results = await db.MatchResults
            .AsNoTracking()
            .Include(result => result.Competitor)
            .Include(result => result.Match)!
            .ThenInclude(match => match.Discipline)
            .Where(result => result.Match != null && result.Match.IsComplete)
            .ToListAsync(cancellationToken);

        return results
            .Where(result => result.Competitor != null && result.Match?.Discipline != null)
            .GroupBy(result => result.Competitor!)
            .Select(group => new StandingRow(
                group.Key.Id,
                group.Key.Name,
                group.Key.ClubOrTeam,
                group.Sum(result => result.Points),
                disciplines.ToDictionary(
                    discipline => discipline,
                    discipline => group
                        .Where(result => result.Match!.Discipline!.Name == discipline)
                        .Sum(result => result.Points))))
            .OrderByDescending(row => row.TotalPoints)
            .ThenBy(row => row.CompetitorName)
            .ToList();
    }
}
