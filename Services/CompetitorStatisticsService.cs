using Competition.Data;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class CompetitorStatisticsService(
    CompetitionDbContext dbContext,
    ICompetitionScoringService scoring) : ICompetitorStatisticsService
{
    public async Task<CompetitorStatistics?> GetAsync(
        long competitorId,
        CancellationToken cancellationToken = default)
    {
        var competitor = await dbContext.Competitors
            .AsNoTracking()
            .Where(item => item.Id == competitorId)
            .Select(item => new { item.Id, item.FirstName, item.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        if (competitor is null)
        {
            return null;
        }

        var entries = await dbContext.CompetitionEntries
            .AsNoTracking()
            .Where(item => item.CompetitorId == competitorId)
            .OrderBy(item => item.CompetitionEdition.StartDate)
            .ThenBy(item => item.CompetitionEdition.EndDate)
            .ThenBy(item => item.CompetitionEditionId)
            .Select(item => new
            {
                EntryId = item.Id,
                EditionId = item.CompetitionEditionId,
                EditionName = item.CompetitionEdition.Name,
                item.CompetitionEdition.StartDate
            })
            .ToListAsync(cancellationToken);

        var disciplineNames = new List<string>();
        var knownDisciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var editionRows = new List<CompetitorEditionStatistics>(entries.Count);

        foreach (var entry in entries)
        {
            var standing = await scoring.GetEditionOverallStandingAsync(entry.EditionId, cancellationToken);
            if (standing is null)
            {
                continue;
            }
            var competitorRow = standing.Rows.SingleOrDefault(row => row.EntryId == entry.EntryId);
            if (competitorRow is null)
            {
                continue;
            }

            foreach (var discipline in standing.Disciplines)
            {
                if (knownDisciplines.Add(discipline.Name))
                {
                    disciplineNames.Add(discipline.Name);
                }
            }

            var cells = competitorRow.Disciplines.Values
                .Join(standing.Disciplines,
                    cell => cell.DisciplineId,
                    discipline => discipline.Id,
                    (cell, discipline) => new
                    {
                        discipline.Name,
                        Cell = new CompetitorStatisticsCell(cell.Rank, cell.Points, cell.TeamName)
                    })
                .ToDictionary(item => item.Name, item => item.Cell, StringComparer.OrdinalIgnoreCase);

            editionRows.Add(new CompetitorEditionStatistics(
                entry.EditionId,
                entry.EditionName,
                entry.StartDate,
                competitorRow.Place,
                competitorRow.TotalPoints,
                cells));
        }

        var totalsByDiscipline = disciplineNames.ToDictionary(
            name => name,
            name => editionRows.Sum(row => row.Disciplines.TryGetValue(name, out var cell) ? cell.Points : 0),
            StringComparer.OrdinalIgnoreCase);

        var byDiscipline = disciplineNames
            .Select(name => new CompetitorDisciplineStatistics(
                name,
                totalsByDiscipline[name],
                editionRows
                    .Where(row => row.Disciplines.ContainsKey(name))
                    .ToDictionary(row => row.EditionId, row => row.Disciplines[name])))
            .OrderByDescending(row => row.TotalPoints)
            .ThenBy(row => row.DisciplineName)
            .ToList();

        var byTeamMember = await BuildTeamMemberStatisticsAsync(competitorId, cancellationToken);

        return new CompetitorStatistics(
            competitor.Id,
            competitor.FirstName,
            competitor.LastName,
            disciplineNames,
            editionRows,
            editionRows.Sum(row => row.TotalPoints),
            totalsByDiscipline,
            byDiscipline,
            byTeamMember);
    }

    private async Task<IReadOnlyList<CompetitorTeamMemberStatistics>> BuildTeamMemberStatisticsAsync(
        long competitorId,
        CancellationToken cancellationToken)
    {
        var sharedTeams = await dbContext.DisciplineTeams
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(team => team.CompetitionDiscipline).ThenInclude(discipline => discipline.Discipline)
            .Include(team => team.FinalStandingEntries)
            .Include(team => team.BonusAwards)
            .Include(team => team.Members).ThenInclude(member => member.CompetitionEntry)
                .ThenInclude(entry => entry.Competitor)
            .Where(team => team.Members.Any(member => member.CompetitionEntry.CompetitorId == competitorId) &&
                           team.Members.Count > 1)
            .ToListAsync(cancellationToken);

        var rows = new Dictionary<long, MutableTeamMemberStatistics>();
        foreach (var team in sharedTeams)
        {
            var disciplineName = team.CompetitionDiscipline.Discipline.Name;
            var points = (team.FinalStandingEntries.SingleOrDefault()?.PointsAwarded ?? 0) +
                team.BonusAwards.Sum(award => award.PointsAwarded);
            foreach (var member in team.Members.Where(member => member.CompetitionEntry.CompetitorId != competitorId))
            {
                var teammate = member.CompetitionEntry.Competitor;
                if (!rows.TryGetValue(teammate.Id, out var row))
                {
                    row = new MutableTeamMemberStatistics(teammate.Id, teammate.FirstName, teammate.LastName);
                    rows.Add(teammate.Id, row);
                }

                row.TotalPoints += points;
                row.Disciplines[disciplineName] =
                    row.Disciplines.GetValueOrDefault(disciplineName) + points;
            }
        }

        return rows.Values
            .OrderByDescending(row => row.TotalPoints)
            .ThenBy(row => row.LastName)
            .ThenBy(row => row.FirstName)
            .Select(row => new CompetitorTeamMemberStatistics(
                row.CompetitorId,
                row.FirstName,
                row.LastName,
                row.TotalPoints,
                row.Disciplines))
            .ToList();
    }

    private sealed class MutableTeamMemberStatistics(long competitorId, string firstName, string lastName)
    {
        public long CompetitorId { get; } = competitorId;
        public string FirstName { get; } = firstName;
        public string LastName { get; } = lastName;
        public int TotalPoints { get; set; }
        public Dictionary<string, int> Disciplines { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
