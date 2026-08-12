using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class CompetitionScoringService(
    CompetitionDbContext dbContext,
    IGroupStandingsService calculatedStandings,
    IAwardPointSystemService pointSystems) : ICompetitionScoringService
{
    public async Task<DisciplineScoringSetup?> GetDisciplineSetupAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines.AsNoTracking()
            .Where(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId)
            .Select(x => new
            {
                x.Id,
                x.IsClosed,
                x.AwardPointSystemId,
                AwardPointSystemName = x.AwardPointSystem == null ? null : x.AwardPointSystem.Name,
                TeamCount = x.Teams.Count,
                HasMatches = x.Phases.SelectMany(p => p.Matches).Any(),
                HasIncompleteMatches = x.Phases.SelectMany(p => p.Matches)
                    .Any(m => m.Status != MatchStatus.Completed || m.HomeScore == null || m.AwayScore == null)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (discipline is null)
        {
            return null;
        }

        var systems = await pointSystems.ListAsync(cancellationToken);
        var finalStandings = await LoadAwardedStandingsAsync(competitionDisciplineId, cancellationToken);
        var blockReason = GetFinalizeBlockReason(discipline.IsClosed, discipline.AwardPointSystemId,
            discipline.TeamCount, discipline.HasMatches, discipline.HasIncompleteMatches);
        if (blockReason is null && finalStandings.Count > 0)
        {
            blockReason = "Nejprve odeberte dříve přidělené body.";
        }

        if (blockReason is null)
        {
            var proposed = await calculatedStandings.GetFinalStandingsAsync(editionId, competitionDisciplineId, cancellationToken);
            if (!HasCompleteRanking(proposed, discipline.TeamCount))
            {
                blockReason = "Z odehraných zápasů zatím nelze určit úplné konečné pořadí.";
            }
        }

        return new DisciplineScoringSetup(
            editionId, discipline.Id, discipline.IsClosed, blockReason is null, blockReason,
            discipline.AwardPointSystemId, discipline.AwardPointSystemName, systems, finalStandings);
    }

    public async Task<bool> SetPointSystemAsync(
        long editionId, long competitionDisciplineId, long? pointSystemId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (discipline.IsLocked)
        {
            throw new ValidationException("Bodovací systém nelze změnit, dokud je disciplína uzamčená.");
        }
        if (pointSystemId is not null && !await dbContext.AwardPointSystems.AnyAsync(x => x.Id == pointSystemId, cancellationToken))
        {
            throw new ValidationException("Vybraný bodovací systém neexistuje.");
        }

        discipline.AwardPointSystemId = pointSystemId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> FinalizeDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            return await FinalizeCoreAsync(editionId, competitionDisciplineId, cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await FinalizeCoreAsync(editionId, competitionDisciplineId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public async Task<bool> ReopenDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        if (!discipline.IsClosed)
        {
            throw new ValidationException("Disciplína už je otevřená.");
        }

        discipline.IsClosed = false;
        discipline.ClosedAtUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CloseDisciplineWithAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Teams)
            .Include(x => x.FinalStandings)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (discipline.FinalStandings.Count == 0)
        {
            throw new ValidationException("Disciplínu lze takto uzavřít jen tehdy, když už má přidělené body.");
        }
        if (discipline.Teams.Count == 0 ||
            discipline.FinalStandings.Count != discipline.Teams.Count ||
            !discipline.FinalStandings.Select(x => x.DisciplineTeamId).ToHashSet()
                .SetEquals(discipline.Teams.Select(x => x.Id)) ||
            discipline.FinalStandings.Any(x => x.Rank <= 0 || x.PointsAwarded < 0))
        {
            throw new ValidationException("Uložené pořadí disciplíny není úplné. Body nejprve odeberte a přidělte znovu.");
        }

        discipline.IsClosed = true;
        discipline.ClosedAtUtc = DateTime.UtcNow;
        discipline.IsLocked = true;
        discipline.IsScheduleLocked = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.FinalStandings)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        if (discipline.IsClosed)
        {
            throw new ValidationException("Před odebráním bodů disciplínu znovu otevřete.");
        }
        if (discipline.FinalStandings.Count == 0)
        {
            throw new ValidationException("Disciplína nemá žádné přidělené body.");
        }

        dbContext.DisciplineStandings.RemoveRange(discipline.FinalStandings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EditionOverallStanding?> GetEditionOverallStandingAsync(
        long editionId, CancellationToken cancellationToken = default)
    {
        var edition = await dbContext.CompetitionEditions.AsNoTracking()
            .Where(x => x.Id == editionId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (edition is null)
        {
            return null;
        }

        var disciplines = await dbContext.CompetitionDisciplines.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Order)
            .Select(x => new EditionStandingDiscipline(x.Id, x.Discipline.Name, x.IsClosed))
            .ToListAsync(cancellationToken);
        var entries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Seed)
            .ThenBy(x => x.Competitor.LastName)
            .ThenBy(x => x.Competitor.FirstName)
            .Select(x => new { x.Id, x.Competitor.FirstName, x.Competitor.LastName })
            .ToListAsync(cancellationToken);
        var awardedMemberships = await dbContext.DisciplineTeamMembers.AsNoTracking()
            .Where(member => member.DisciplineTeam.CompetitionDiscipline.CompetitionEditionId == editionId &&
                member.DisciplineTeam.FinalStandingEntries.Any())
            .OrderBy(member => member.Order)
            .Select(member => new
            {
                EntryId = member.CompetitionEntryId,
                TeamId = member.DisciplineTeamId,
                member.DisciplineTeam.CompetitionDisciplineId,
                member.CompetitionEntry.Competitor.LastName,
                Rank = member.DisciplineTeam.FinalStandingEntries
                    .Where(standing => standing.CompetitionDisciplineId == member.DisciplineTeam.CompetitionDisciplineId)
                    .Select(standing => standing.Rank)
                    .Single(),
                PointsAwarded = member.DisciplineTeam.FinalStandingEntries
                    .Where(standing => standing.CompetitionDisciplineId == member.DisciplineTeam.CompetitionDisciplineId)
                    .Select(standing => standing.PointsAwarded)
                    .Single()
            })
            .ToListAsync(cancellationToken);
        var teamNames = awardedMemberships.GroupBy(x => x.TeamId)
            .ToDictionary(group => group.Key, group => string.Join("/", group.Select(x => x.LastName)));
        var maxRank = awardedMemberships.Select(x => x.Rank).DefaultIfEmpty(0).Max();

        var cellsByEntry = awardedMemberships.GroupBy(x => x.EntryId).ToDictionary(
            group => group.Key,
            group => group.Select(cell => new EditionStandingCell(
                    cell.CompetitionDisciplineId,
                    teamNames.GetValueOrDefault(cell.TeamId, string.Empty),
                    cell.Rank,
                    cell.PointsAwarded))
                .ToDictionary(cell => cell.DisciplineId));

        var mutableRows = entries.Select(entry =>
        {
            var cells = cellsByEntry.GetValueOrDefault(entry.Id) ??
                new Dictionary<long, EditionStandingCell>();
            var rankCounts = Enumerable.Range(1, maxRank)
                .Select(rank => cells.Values.Count(cell => cell.Rank == rank)).ToArray();
            return new MutableOverallRow(entry.Id, entry.FirstName, entry.LastName,
                cells, cells.Values.Sum(x => x.Points), rankCounts);
        }).ToList();

        mutableRows.Sort(CompareRows);
        var rows = new List<EditionStandingRow>(mutableRows.Count);
        for (var index = 0; index < mutableRows.Count; index++)
        {
            var place = index == 0 || !HasSameCompetitiveResult(mutableRows[index - 1], mutableRows[index])
                ? index + 1
                : rows[index - 1].Place;
            var row = mutableRows[index];
            rows.Add(new EditionStandingRow(place, row.EntryId, row.FirstName, row.LastName,
                row.Disciplines, row.TotalPoints));
        }

        return new EditionOverallStanding(edition.Id, edition.Name, disciplines, rows);
    }

    private async Task<bool> FinalizeCoreAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Teams).ThenInclude(x => x.Members)
            .Include(x => x.AwardPointSystem).ThenInclude(x => x!.Rules)
            .Include(x => x.Phases).ThenInclude(x => x.Matches)
            .Include(x => x.FinalStandings)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (discipline.AwardPointSystem is null)
        {
            throw new ValidationException("Nejprve vyberte bodovací systém.");
        }
        if (discipline.FinalStandings.Count > 0)
        {
            throw new ValidationException("Nejprve odeberte dříve přidělené body.");
        }

        var matches = discipline.Phases.SelectMany(x => x.Matches).ToList();
        if (matches.Count == 0 || matches.Any(x => x.Status != MatchStatus.Completed || x.HomeScore is null || x.AwayScore is null))
        {
            throw new ValidationException("Body lze přidělit až po odehrání všech zápasů disciplíny.");
        }
        if (discipline.Teams.Count == 0 || discipline.Teams.Any(x => x.Members.Count == 0))
        {
            throw new ValidationException("Každý tým musí mít alespoň jednoho člena.");
        }

        var proposed = await calculatedStandings.GetFinalStandingsAsync(editionId, competitionDisciplineId, cancellationToken);
        if (!HasCompleteRanking(proposed, discipline.Teams.Count) ||
            !proposed!.Rows.Select(x => x.TeamId).ToHashSet().SetEquals(discipline.Teams.Select(x => x.Id)))
        {
            throw new ValidationException("Z odehraných zápasů nelze určit úplné konečné pořadí všech týmů.");
        }

        var finalRows = proposed!.Rows;
        var rules = discipline.AwardPointSystem.Rules.ToDictionary(x => x.Rank);

        dbContext.DisciplineStandings.RemoveRange(discipline.FinalStandings);
        foreach (var row in finalRows)
        {
            dbContext.DisciplineStandings.Add(new DisciplineStanding
            {
                CompetitionDisciplineId = discipline.Id,
                DisciplineTeamId = row.TeamId,
                Rank = row.Position,
                PointsAwarded = rules.TryGetValue(row.Position, out var rule) ? rule.Points : 0
            });
        }
        discipline.IsClosed = true;
        discipline.ClosedAtUtc = DateTime.UtcNow;
        discipline.IsLocked = true;
        discipline.IsScheduleLocked = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<DisciplineAwardedStanding>> LoadAwardedStandingsAsync(
        long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var teams = await dbContext.DisciplineTeams.AsNoTrackingWithIdentityResolution()
            .Where(team => team.CompetitionDisciplineId == competitionDisciplineId)
            .Include(team => team.Members).ThenInclude(member => member.CompetitionEntry)
                .ThenInclude(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var standings = await dbContext.DisciplineStandings.AsNoTracking()
            .Where(standing => standing.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(standing => standing.Rank)
            .ToListAsync(cancellationToken);
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEdition.Disciplines.Any(
                discipline => discipline.Id == competitionDisciplineId))
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var teamById = teams.ToDictionary(team => team.Id);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);
        return standings.Where(standing => teamById.ContainsKey(standing.DisciplineTeamId))
            .Select(standing => new DisciplineAwardedStanding(
                standing.Rank,
                standing.DisciplineTeamId,
                TeamNameFormatter.Format(teamById[standing.DisciplineTeamId], entryLabels),
                standing.PointsAwarded))
            .ToList();
    }

    private static string? GetFinalizeBlockReason(
        bool isClosed, long? pointSystemId, int teamCount, bool hasMatches, bool hasIncompleteMatches)
    {
        if (isClosed) return "Disciplína už je uzavřená.";
        if (pointSystemId is null) return "Nejprve vyberte bodovací systém.";
        if (teamCount == 0) return "Disciplína nemá žádné týmy.";
        if (!hasMatches) return "Disciplína nemá žádné zápasy.";
        if (hasIncompleteMatches) return "Nejprve odehrajte všechny zápasy disciplíny.";
        return null;
    }

    private static void EnsureOpen(CompetitionDiscipline discipline)
    {
        if (discipline.IsClosed)
        {
            throw new ValidationException("Uzavřenou disciplínu už nelze měnit.");
        }
    }

    private static bool HasCompleteRanking(FinalStandingTable? standing, int teamCount) =>
        standing is not null &&
        standing.Rows.Count == teamCount &&
        standing.Rows.Select(x => x.TeamId).Distinct().Count() == teamCount &&
        standing.Rows.Select(x => x.Position).Distinct().Count() == teamCount &&
        standing.Rows.OrderBy(x => x.Position).Select(x => x.Position)
            .SequenceEqual(Enumerable.Range(1, teamCount));

    private static int CompareRows(MutableOverallRow left, MutableOverallRow right)
    {
        var comparison = right.TotalPoints.CompareTo(left.TotalPoints);
        if (comparison != 0) return comparison;
        for (var index = 0; index < left.RankCounts.Length; index++)
        {
            comparison = right.RankCounts[index].CompareTo(left.RankCounts[index]);
            if (comparison != 0) return comparison;
        }
        comparison = string.Compare(left.LastName, right.LastName, StringComparison.CurrentCultureIgnoreCase);
        return comparison != 0
            ? comparison
            : string.Compare(left.FirstName, right.FirstName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool HasSameCompetitiveResult(MutableOverallRow left, MutableOverallRow right) =>
        left.TotalPoints == right.TotalPoints && left.RankCounts.SequenceEqual(right.RankCounts);

    private sealed record MutableOverallRow(
        long EntryId,
        string FirstName,
        string LastName,
        IReadOnlyDictionary<long, EditionStandingCell> Disciplines,
        int TotalPoints,
        int[] RankCounts);
}
