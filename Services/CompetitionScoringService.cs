using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
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
        var bonusRules = await dbContext.DisciplineBonusPointRules.AsNoTracking()
            .Where(rule => rule.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(rule => rule.Type)
            .Select(rule => new BonusPointRuleItem(rule.Type, rule.Points))
            .ToListAsync(cancellationToken);
        var bonusAwards = await LoadBonusAwardsAsync(competitionDisciplineId, cancellationToken);
        var bonusMetricStandings = bonusRules.Count == 0 || bonusAwards.Count == 0
            ? []
            : await LoadBonusMetricStandingsAsync(competitionDisciplineId, bonusRules, cancellationToken);
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
            discipline.AwardPointSystemId, discipline.AwardPointSystemName, systems, finalStandings,
            bonusRules, bonusAwards, bonusMetricStandings);
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

    public async Task<bool> SetBonusPointRulesAsync(
        long editionId,
        long competitionDisciplineId,
        IReadOnlyCollection<BonusPointRuleInput> rules,
        CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.BonusPointRules)
            .Include(x => x.FinalStandings)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId,
                cancellationToken);
        if (discipline is null)
        {
            return false;
        }

        EnsureOpen(discipline);
        if (discipline.IsLocked)
        {
            throw new ValidationException("Bonusové body nelze změnit, dokud je disciplína uzamčená.");
        }
        if (discipline.FinalStandings.Count > 0)
        {
            throw new ValidationException("Před změnou bonusových bodů odeberte dříve přidělené body.");
        }

        var enabled = rules.Where(rule => rule.Enabled).ToList();
        if (enabled.GroupBy(rule => rule.Type).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Každý typ bonusových bodů lze u disciplíny nastavit jen jednou.");
        }
        if (enabled.Any(rule => rule.Points is < 0 or > 1000))
        {
            throw new ValidationException("Počet bonusových bodů musí být mezi 0 a 1000.");
        }
        if (!discipline.UsesSetScores && enabled.Any(rule =>
                rule.Type == BonusPointType.LowestAverageSubscoreAgainst))
        {
            throw new ValidationException("Bonus za dílčí skóre lze nastavit jen u disciplíny používající sety.");
        }

        var enabledByType = enabled.ToDictionary(rule => rule.Type);
        foreach (var existingRule in discipline.BonusPointRules.ToList())
        {
            if (enabledByType.Remove(existingRule.Type, out var replacement))
            {
                existingRule.Points = replacement.Points;
            }
            else
            {
                dbContext.DisciplineBonusPointRules.Remove(existingRule);
            }
        }
        foreach (var rule in enabledByType.Values)
        {
            dbContext.DisciplineBonusPointRules.Add(new DisciplineBonusPointRule
            {
                CompetitionDisciplineId = discipline.Id,
                Type = rule.Type,
                Points = rule.Points
            });
        }
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
            .Include(x => x.BonusAwards)
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

        dbContext.DisciplineBonusAwards.RemoveRange(discipline.BonusAwards);
        dbContext.DisciplineStandings.RemoveRange(discipline.FinalStandings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EditionOverallStanding?> GetEditionOverallStandingAsync(
        long editionId, CancellationToken cancellationToken = default, bool includeBonusPoints = true)
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
                    .Single(),
                BonusPoints = member.DisciplineTeam.BonusAwards
                    .Where(award => award.CompetitionDisciplineId == member.DisciplineTeam.CompetitionDisciplineId)
                    .Sum(award => award.PointsAwarded)
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
                    cell.PointsAwarded + (includeBonusPoints ? cell.BonusPoints : 0),
                    cell.PointsAwarded,
                    cell.BonusPoints))
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
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .Include(x => x.FinalStandings)
            .Include(x => x.BonusPointRules)
            .Include(x => x.BonusAwards)
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
        if (discipline.BonusPointRules.Any(rule =>
                rule.Type == BonusPointType.LowestAverageSubscoreAgainst) &&
            matches.Any(match => match.SetScores.Count == 0))
        {
            throw new ValidationException("Pro bonus za dílčí skóre musí mít každý zápas vyplněné výsledky setů.");
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
        dbContext.DisciplineBonusAwards.RemoveRange(discipline.BonusAwards);
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
        foreach (var award in CalculateBonusAwards(discipline, matches))
        {
            dbContext.DisciplineBonusAwards.Add(award);
        }
        discipline.IsClosed = true;
        discipline.ClosedAtUtc = DateTime.UtcNow;
        discipline.IsLocked = true;
        discipline.IsScheduleLocked = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IReadOnlyList<DisciplineBonusAward> CalculateBonusAwards(
        CompetitionDiscipline discipline,
        IReadOnlyCollection<Match> matches)
    {
        if (discipline.BonusPointRules.Count == 0)
        {
            return [];
        }

        var metrics = BuildTeamBonusMetrics(discipline.Teams.Select(team => team.Id), matches);

        var eligible = metrics.Where(item => item.Value.MatchCount > 0).ToList();
        var result = new List<DisciplineBonusAward>();
        foreach (var rule in discipline.BonusPointRules)
        {
            if (rule.Type == BonusPointType.LowestAverageSubscoreAgainst && !discipline.UsesSetScores)
            {
                continue;
            }

            var winners = eligible.Where(candidate => eligible.All(other =>
                CompareAverage(
                    GetMetric(candidate.Value, rule.Type), candidate.Value.MatchCount,
                    GetMetric(other.Value, rule.Type), other.Value.MatchCount,
                    rule.Type == BonusPointType.LowestAverageSubscoreAgainst) >= 0));
            foreach (var winner in winners)
            {
                result.Add(new DisciplineBonusAward
                {
                    CompetitionDisciplineId = discipline.Id,
                    DisciplineTeamId = winner.Key,
                    Type = rule.Type,
                    PointsAwarded = rule.Points,
                    MetricTotal = GetMetric(winner.Value, rule.Type),
                    MatchCount = winner.Value.MatchCount
                });
            }
        }

        return result;
    }

    private static Dictionary<long, TeamBonusMetrics> BuildTeamBonusMetrics(
        IEnumerable<long> teamIds,
        IEnumerable<Match> matches)
    {
        var metrics = teamIds.ToDictionary(teamId => teamId, _ => new TeamBonusMetrics());
        foreach (var match in matches.Where(match =>
                     match.Status == MatchStatus.Completed &&
                     match.HomeScore is not null && match.AwayScore is not null &&
                     match.HomeTeamId is not null && match.AwayTeamId is not null &&
                     match.HomeTeamId != match.AwayTeamId &&
                     metrics.ContainsKey(match.HomeTeamId.Value) && metrics.ContainsKey(match.AwayTeamId.Value)))
        {
            var home = metrics[match.HomeTeamId!.Value];
            var away = metrics[match.AwayTeamId!.Value];
            var homeScore = match.HomeScore!.Value;
            var awayScore = match.AwayScore!.Value;
            var homeSubscore = match.SetScores.Sum(set => set.HomeScore);
            var awaySubscore = match.SetScores.Sum(set => set.AwayScore);

            home.MatchCount++;
            away.MatchCount++;
            home.ScoreFor += homeScore;
            away.ScoreFor += awayScore;
            home.ScoreDifference += homeScore - awayScore;
            away.ScoreDifference += awayScore - homeScore;
            home.SubscoreAgainst += awaySubscore;
            away.SubscoreAgainst += homeSubscore;
        }

        return metrics;
    }

    private static int GetMetric(TeamBonusMetrics metrics, BonusPointType type) => type switch
    {
        BonusPointType.LowestAverageSubscoreAgainst => metrics.SubscoreAgainst,
        BonusPointType.HighestAverageScoreFor => metrics.ScoreFor,
        BonusPointType.HighestAverageScoreDifference => metrics.ScoreDifference,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static int CompareAverage(
        int leftTotal, int leftCount, int rightTotal, int rightCount, bool lowerIsBetter)
    {
        var comparison = ((long)leftTotal * rightCount).CompareTo((long)rightTotal * leftCount);
        return lowerIsBetter ? -comparison : comparison;
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

    private async Task<IReadOnlyList<DisciplineBonusAwardItem>> LoadBonusAwardsAsync(
        long competitionDisciplineId,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.DisciplineTeams.AsNoTrackingWithIdentityResolution()
            .Where(team => team.CompetitionDisciplineId == competitionDisciplineId)
            .Include(team => team.Members).ThenInclude(member => member.CompetitionEntry)
                .ThenInclude(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var awards = await dbContext.DisciplineBonusAwards.AsNoTracking()
            .Where(award => award.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(award => award.Type)
            .ThenBy(award => award.DisciplineTeamId)
            .ToListAsync(cancellationToken);
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEdition.Disciplines.Any(
                discipline => discipline.Id == competitionDisciplineId))
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var teamById = teams.ToDictionary(team => team.Id);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);
        return awards.Where(award => teamById.ContainsKey(award.DisciplineTeamId))
            .Select(award => new DisciplineBonusAwardItem(
                award.Type,
                award.PointsAwarded,
                award.DisciplineTeamId,
                TeamNameFormatter.Format(teamById[award.DisciplineTeamId], entryLabels),
                award.MetricTotal,
                award.MatchCount))
            .ToList();
    }

    private async Task<IReadOnlyList<DisciplineBonusMetricStanding>> LoadBonusMetricStandingsAsync(
        long competitionDisciplineId,
        IReadOnlyCollection<BonusPointRuleItem> rules,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.DisciplineTeams.AsNoTrackingWithIdentityResolution()
            .Where(team => team.CompetitionDisciplineId == competitionDisciplineId)
            .Include(team => team.Members).ThenInclude(member => member.CompetitionEntry)
                .ThenInclude(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var matches = await dbContext.Matches.AsNoTracking()
            .Where(match => match.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId)
            .Include(match => match.SetScores)
            .ToListAsync(cancellationToken);
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEdition.Disciplines.Any(
                discipline => discipline.Id == competitionDisciplineId))
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);
        var teamNames = teams.ToDictionary(team => team.Id, team => TeamNameFormatter.Format(team, entryLabels));
        var metrics = BuildTeamBonusMetrics(teamNames.Keys, matches);
        var result = new List<DisciplineBonusMetricStanding>();

        foreach (var rule in rules)
        {
            var ranked = metrics.Where(item => item.Value.MatchCount > 0).ToList();
            ranked.Sort((left, right) => -CompareAverage(
                GetMetric(left.Value, rule.Type), left.Value.MatchCount,
                GetMetric(right.Value, rule.Type), right.Value.MatchCount,
                rule.Type == BonusPointType.LowestAverageSubscoreAgainst));

            var rank = 0;
            KeyValuePair<long, TeamBonusMetrics>? previous = null;
            for (var index = 0; index < ranked.Count; index++)
            {
                var current = ranked[index];
                if (previous is null || CompareAverage(
                        GetMetric(current.Value, rule.Type), current.Value.MatchCount,
                        GetMetric(previous.Value.Value, rule.Type), previous.Value.Value.MatchCount,
                        rule.Type == BonusPointType.LowestAverageSubscoreAgainst) != 0)
                {
                    rank = index + 1;
                }

                result.Add(new DisciplineBonusMetricStanding(
                    rule.Type,
                    rank,
                    current.Key,
                    teamNames[current.Key],
                    GetMetric(current.Value, rule.Type),
                    current.Value.MatchCount));
                previous = current;
            }
        }

        return result;
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

    private sealed class TeamBonusMetrics
    {
        public int MatchCount { get; set; }
        public int ScoreFor { get; set; }
        public int ScoreDifference { get; set; }
        public int SubscoreAgainst { get; set; }
    }
}
