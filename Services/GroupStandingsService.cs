using Competition.Data;
using Competition.Domain;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class GroupStandingsService(CompetitionDbContext dbContext) : IGroupStandingsService
{
    public async Task<IReadOnlyList<GroupStandingTable>> GetForDisciplineAsync(
        long editionId,
        long competitionDisciplineId,
        CancellationToken cancellationToken = default)
    {
        var groups = await dbContext.PhaseGroups
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(group =>
                group.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                group.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId &&
                group.DisciplinePhase.Type == PhaseType.Group)
            .Include(group => group.DisciplinePhase)
                .ThenInclude(phase => phase.CompetitionDiscipline)
            .Include(group => group.Teams)
                .ThenInclude(assignment => assignment.DisciplineTeam)
                    .ThenInclude(team => team.Members)
                        .ThenInclude(member => member.CompetitionEntry)
                            .ThenInclude(entry => entry.Competitor)
            .Include(group => group.Matches)
                .ThenInclude(match => match.SetScores)
            .OrderBy(group => group.DisciplinePhase.Order)
            .ThenBy(group => group.Order)
            .ToListAsync(cancellationToken);

        var teams = groups.SelectMany(group => group.Teams)
            .Select(assignment => assignment.DisciplineTeam)
            .DistinctBy(team => team.Id)
            .ToList();
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEditionId == editionId)
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);
        return groups.Select(group =>
        {
            var table = Calculate(group, group.DisciplinePhase.CompetitionDiscipline.UsesSetScores);
            return table with
            {
                Rows = table.Rows.Select(row => row with
                {
                    TeamName = TeamNameFormatter.Format(
                        teams.Single(team => team.Id == row.TeamId), entryLabels)
                }).ToList()
            };
        }).ToList();
    }

    public async Task<FinalStandingTable?> GetFinalStandingsAsync(
        long editionId,
        long competitionDisciplineId,
        CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(item => item.Id == competitionDisciplineId && item.CompetitionEditionId == editionId)
            .Include(item => item.Teams)
                .ThenInclude(team => team.Members)
                    .ThenInclude(member => member.CompetitionEntry)
                        .ThenInclude(entry => entry.Competitor)
            .Include(item => item.FinalStandings)
            .Include(item => item.Phases)
                .ThenInclude(phase => phase.Groups)
                    .ThenInclude(group => group.Teams)
            .Include(item => item.Phases)
                .ThenInclude(phase => phase.Matches)
                    .ThenInclude(match => match.SetScores)
            .SingleOrDefaultAsync(cancellationToken);

        if (discipline is null)
        {
            return null;
        }
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEditionId == editionId)
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);

        var rows = discipline.PlayingSystem switch
        {
            PlayingSystemType.RoundRobin => CalculateRoundRobinFinalStandings(discipline),
            PlayingSystemType.GroupsThenClassificationMatches => CalculateGroupClassificationFinalStandings(discipline),
            PlayingSystemType.Knockout => CalculateKnockoutFinalStandings(discipline),
            PlayingSystemType.RoundRobinThenKnockout => CalculateCombinedFinalStandings(discipline),
            PlayingSystemType.Custom => CalculateCustomFinalStandings(discipline),
            _ => []
        };
        rows = ApplyAllMatchAggregates(rows, discipline);

        if (discipline.FinalStandings.Count != 0)
        {
            var teams = discipline.Teams.ToDictionary(x => x.Id);
            var calculatedByTeam = rows.ToDictionary(x => x.TeamId);
            var finalizedRows = discipline.FinalStandings.OrderBy(x => x.Rank)
                .Where(x => teams.ContainsKey(x.DisciplineTeamId))
                .Select(x => calculatedByTeam.TryGetValue(x.DisciplineTeamId, out var calculated)
                    ? calculated with { Position = x.Rank, PointsAwarded = x.PointsAwarded }
                    : new FinalStandingRow(
                        x.Rank,
                        x.DisciplineTeamId,
                        TeamName(teams[x.DisciplineTeamId]),
                        teams[x.DisciplineTeamId].Seed,
                        "uzavřeno",
                        0, 0, 0, 0,
                        x.PointsAwarded))
                .ToList();
            return ApplyTeamNames(new FinalStandingTable(discipline.UsesSetScores, finalizedRows, true), discipline, entryLabels);
        }

        return rows.Count == 0
            ? null
            : ApplyTeamNames(new FinalStandingTable(discipline.UsesSetScores, rows), discipline, entryLabels);
    }

    private static FinalStandingTable ApplyTeamNames(
        FinalStandingTable table,
        CompetitionDiscipline discipline,
        IReadOnlyDictionary<long, string> entryLabels)
    {
        var teams = discipline.Teams.ToDictionary(team => team.Id);
        return table with
        {
            Rows = table.Rows.Select(row => row with
            {
                TeamName = teams.TryGetValue(row.TeamId, out var team)
                    ? TeamNameFormatter.Format(team, entryLabels)
                    : row.TeamName
            }).ToList()
        };
    }

    private static IReadOnlyList<FinalStandingRow> ApplyAllMatchAggregates(
        IReadOnlyList<FinalStandingRow> rows,
        CompetitionDiscipline discipline)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var teamIds = discipline.Teams.Select(team => team.Id).ToHashSet();
        var completedMatches = discipline.Phases
            .SelectMany(phase => phase.Matches)
            .Where(match => IsCompletedMatch(match) &&
                teamIds.Contains(match.HomeTeamId!.Value) &&
                teamIds.Contains(match.AwayTeamId!.Value))
            .ToList();
        var aggregates = BuildMatchAggregates(teamIds, completedMatches);
        return rows.Select(row =>
        {
            var aggregate = aggregates[row.TeamId];
            return row with
            {
                ScoreFor = aggregate.ScoreFor,
                ScoreAgainst = aggregate.ScoreAgainst,
                SubscoreFor = aggregate.SubscoreFor,
                SubscoreAgainst = aggregate.SubscoreAgainst
            };
        }).ToList();
    }

    private static IReadOnlyList<FinalStandingRow> CalculateRoundRobinFinalStandings(CompetitionDiscipline discipline)
    {
        var groups = discipline.Phases
            .Where(phase => phase.Type == PhaseType.Group)
            .OrderBy(phase => phase.Order)
            .SelectMany(phase => phase.Groups.OrderBy(group => group.Order))
            .ToList();

        if (groups.Count != 1 || !IsGroupComplete(groups[0]))
        {
            return [];
        }

        return Calculate(groups[0], discipline.UsesSetScores).Rows
            .Select(row => ToFinalRow(row, "skupinová tabulka")).ToList();
    }

    private static IReadOnlyList<FinalStandingRow> CalculateGroupClassificationFinalStandings(
        CompetitionDiscipline discipline)
    {
        var groups = discipline.Phases
            .Where(phase => phase.Type == PhaseType.Group)
            .OrderBy(phase => phase.Order)
            .SelectMany(phase => phase.Groups.OrderBy(group => group.Order))
            .ToList();
        if (groups.Count == 0 || groups.Any(group => !IsGroupComplete(group)))
        {
            return [];
        }

        var tables = groups.Select(group => Calculate(group, discipline.UsesSetScores))
            .ToDictionary(table => table.GroupId);
        var teamRows = tables.Values.SelectMany(table => table.Rows).ToDictionary(row => row.TeamId);
        var finalMatches = discipline.Phases
            .Where(phase => phase.Type == PhaseType.FinalStanding)
            .OrderBy(phase => phase.Order)
            .SelectMany(phase => phase.Matches.OrderBy(match => match.Order))
            .ToList();

        var rows = new List<FinalStandingRow>();
        var placedTeamIds = new HashSet<long>();
        foreach (var match in finalMatches.Where(IsCompletedMatch))
        {
            var winnerId = WinnerId(match);
            var loserId = LoserId(match);
            if (winnerId is null || loserId is null)
            {
                continue;
            }

            var firstPosition = (match.Order - 1) * 2 + 1;
            AddClassificationRow(rows, placedTeamIds, teamRows, discipline, match, winnerId.Value, firstPosition);
            AddClassificationRow(rows, placedTeamIds, teamRows, discipline, match, loserId.Value, firstPosition + 1);
        }

        var advancingTeamIds = finalMatches
            .SelectMany(match => new[]
            {
                ResolveGroupSourceTeamId(match.HomeSourceGroupId, match.HomeSourceRank, tables),
                ResolveGroupSourceTeamId(match.AwaySourceGroupId, match.AwaySourceRank, tables)
            })
            .OfType<long>()
            .ToHashSet();

        var nonAdvancing = tables.Values
            .SelectMany(table => table.Rows)
            .Where(row => !advancingTeamIds.Contains(row.TeamId) && !placedTeamIds.Contains(row.TeamId))
            .GroupBy(row => row.Position)
            .OrderBy(group => group.Key);
        var nextPosition = Math.Max(advancingTeamIds.Count + 1, rows.Count == 0 ? 1 : rows.Max(row => row.Position) + 1);
        foreach (var sameGroupPlace in nonAdvancing)
        {
            foreach (var row in OrderGroupRowsForCrossGroupPlacement(
                         sameGroupPlace,
                         discipline.UsesSetScores))
            {
                rows.Add(ToFinalRow(row, $"{row.Position}. místo ve skupině", nextPosition++));
            }
        }

        return rows.OrderBy(row => row.Position).ToList();
    }

    private static IReadOnlyList<FinalStandingRow> CalculateKnockoutFinalStandings(CompetitionDiscipline discipline)
    {
        var stages = discipline.Phases
            .Where(phase => phase.Type == PhaseType.Knockout)
            .OrderBy(phase => phase.Order)
            .SelectMany(phase => phase.Groups.OrderBy(group => group.Order))
            .ToList();
        if (stages.Count == 0)
        {
            return [];
        }

        var teamById = discipline.Teams.ToDictionary(team => team.Id);
        var allKnockoutMatches = stages.SelectMany(stage => stage.Matches).Where(IsCompletedMatch).ToList();
        var knockoutTeamCount = allKnockoutMatches
            .SelectMany(match => new[] { match.HomeTeamId, match.AwayTeamId })
            .OfType<long>()
            .Distinct()
            .Count();
        var aggregates = BuildMatchAggregates(teamById.Keys, allKnockoutMatches);
        var rows = new List<FinalStandingRow>();

        for (var stageIndex = stages.Count - 1; stageIndex >= 0; stageIndex--)
        {
            var stage = stages[stageIndex];
            var matches = stage.Matches.OrderBy(match => match.Order).ToList();
            if (matches.Count == 0 || matches.Any(match => !IsCompletedMatch(match)))
            {
                continue;
            }

            var losers = matches.Select(LoserId).OfType<long>().Distinct().ToList();
            if (losers.Count != matches.Count)
            {
                continue;
            }

            if (stageIndex == stages.Count - 1 && matches.Count == 1)
            {
                var winnerId = WinnerId(matches[0]);
                if (winnerId is not null && teamById.TryGetValue(winnerId.Value, out var winner))
                {
                    rows.Add(ToKnockoutRow(1, winner, aggregates[winner.Id], stage.Name));
                }
            }

            var earlierLoserCount = stages
                .Take(stageIndex + 1)
                .SelectMany(item => item.Matches)
                .Where(IsCompletedMatch)
                .Select(LoserId)
                .OfType<long>()
                .Distinct()
                .Count();
            var firstLoserPosition = knockoutTeamCount - earlierLoserCount + 1;

            var orderedLosers = losers
                .Where(teamById.ContainsKey)
                .OrderByDescending(teamId => aggregates[teamId].ScoreDifference)
                .ThenByDescending(teamId => discipline.UsesSetScores
                    ? aggregates[teamId].SubscoreDifference
                    : 0)
                .ThenByDescending(teamId => aggregates[teamId].ScoreFor)
                .ThenByDescending(teamId => discipline.UsesSetScores
                    ? aggregates[teamId].SubscoreFor
                    : 0)
                .ThenBy(teamId => teamById[teamId].Seed)
                .ThenBy(teamId => teamId)
                .ToList();

            foreach (var teamId in orderedLosers)
            {
                if (rows.Any(row => row.TeamId == teamId))
                {
                    continue;
                }

                rows.Add(ToKnockoutRow(firstLoserPosition++, teamById[teamId], aggregates[teamId], stage.Name));
            }
        }

        return rows.OrderBy(row => row.Position).ToList();
    }

    private static IReadOnlyList<FinalStandingRow> CalculateCombinedFinalStandings(CompetitionDiscipline discipline)
    {
        var knockoutRows = CalculateKnockoutFinalStandings(discipline).ToList();
        if (knockoutRows.Count == 0)
        {
            return CalculateRoundRobinFinalStandings(discipline);
        }

        var placedTeamIds = knockoutRows.Select(x => x.TeamId).ToHashSet();
        var groupRows = discipline.Phases
            .Where(phase => phase.Type == PhaseType.Group)
            .OrderByDescending(phase => phase.Order)
            .SelectMany(phase => phase.Groups.OrderBy(group => group.Order))
            .Where(IsGroupComplete)
            .SelectMany(group => Calculate(group, discipline.UsesSetScores).Rows)
            .Where(row => !placedTeamIds.Contains(row.TeamId))
            .GroupBy(row => row.TeamId)
            .Select(group => group.First())
            .OrderBy(row => row.Position)
            .ThenByDescending(row => row.TablePoints)
            .ThenByDescending(row => row.ScoreDifference)
            .ThenByDescending(row => discipline.UsesSetScores ? row.SubscoreDifference : 0)
            .ThenByDescending(row => row.ScoreFor)
            .ThenByDescending(row => discipline.UsesSetScores ? row.SubscoreFor : 0)
            .ThenBy(row => row.Seed)
            .ThenBy(row => row.TeamId)
            .ToList();

        var nextPosition = knockoutRows.Count + 1;
        knockoutRows.AddRange(groupRows.Select(row =>
            ToFinalRow(row, $"{row.Position}. místo ve skupině", nextPosition++)));
        return knockoutRows.OrderBy(row => row.Position).ToList();
    }

    private static IReadOnlyList<FinalStandingRow> CalculateCustomFinalStandings(CompetitionDiscipline discipline)
    {
        if (discipline.Phases.Any(x => x.Type == PhaseType.Knockout))
        {
            return CalculateCombinedFinalStandings(discipline);
        }

        var placementMatches = discipline.Phases
            .Where(x => x.Type == PhaseType.FinalStanding)
            .OrderBy(x => x.Order)
            .SelectMany(x => x.Matches.OrderBy(match => match.Order))
            .ToList();
        if (placementMatches.Count != 0 && placementMatches.All(IsCompletedMatch))
        {
            var teamById = discipline.Teams.ToDictionary(x => x.Id);
            var rows = new List<FinalStandingRow>();
            foreach (var match in placementMatches)
            {
                var winnerId = WinnerId(match);
                var loserId = LoserId(match);
                if (winnerId is null || loserId is null ||
                    !teamById.TryGetValue(winnerId.Value, out var winner) ||
                    !teamById.TryGetValue(loserId.Value, out var loser))
                {
                    return [];
                }
                var firstPosition = (match.Order - 1) * 2 + 1;
                rows.Add(ToPlacementMatchRow(firstPosition, winner, match));
                rows.Add(ToPlacementMatchRow(firstPosition + 1, loser, match));
            }
            if (rows.Select(x => x.TeamId).Distinct().Count() == discipline.Teams.Count)
            {
                return rows.OrderBy(x => x.Position).ToList();
            }
        }

        return CalculateRoundRobinFinalStandings(discipline);
    }

    private static FinalStandingRow ToPlacementMatchRow(int position, DisciplineTeam team, Match match) =>
        new(position, team.Id, TeamName(team), team.Seed, match.Name,
            team.Id == match.HomeTeamId ? match.HomeScore!.Value : match.AwayScore!.Value,
            team.Id == match.HomeTeamId ? match.AwayScore!.Value : match.HomeScore!.Value,
            team.Id == match.HomeTeamId ? match.SetScores.Sum(x => x.HomeScore) : match.SetScores.Sum(x => x.AwayScore),
            team.Id == match.HomeTeamId ? match.SetScores.Sum(x => x.AwayScore) : match.SetScores.Sum(x => x.HomeScore));

    private static GroupStandingTable Calculate(PhaseGroup group, bool usesSetScores)
    {
        var teams = group.Teams
            .OrderBy(assignment => assignment.DisciplineTeam.Seed)
            .ThenBy(assignment => assignment.DisciplineTeamId)
            .Select(assignment => new MutableStanding(
                assignment.DisciplineTeamId,
                TeamName(assignment.DisciplineTeam),
                assignment.DisciplineTeam.Seed))
            .ToDictionary(standing => standing.TeamId);

        var completedMatches = group.Matches.Where(match => IsCompletedMatch(match) &&
                match.HomeTeamId != match.AwayTeamId &&
                teams.ContainsKey(match.HomeTeamId!.Value) &&
                teams.ContainsKey(match.AwayTeamId!.Value))
            .ToList();

        foreach (var match in completedMatches)
        {
            ApplyMatch(teams[match.HomeTeamId!.Value], teams[match.AwayTeamId!.Value], match,
                group.DisciplinePhase.PointsForWin, group.DisciplinePhase.PointsForDraw,
                group.DisciplinePhase.PointsForLoss, StandingScope.Overall);
        }

        foreach (var tiedTeams in teams.Values.GroupBy(standing => standing.TablePoints))
        {
            var tiedTeamIds = tiedTeams.Select(standing => standing.TeamId).ToHashSet();
            if (tiedTeamIds.Count < 2)
            {
                continue;
            }

            foreach (var match in completedMatches.Where(match =>
                         tiedTeamIds.Contains(match.HomeTeamId!.Value) &&
                         tiedTeamIds.Contains(match.AwayTeamId!.Value)))
            {
                ApplyMatch(teams[match.HomeTeamId!.Value], teams[match.AwayTeamId!.Value], match,
                    group.DisciplinePhase.PointsForWin, group.DisciplinePhase.PointsForDraw,
                    group.DisciplinePhase.PointsForLoss, StandingScope.MiniTable);
            }
        }

        var ordered = teams.Values
            .OrderByDescending(standing => standing.TablePoints)
            .ThenByDescending(standing => standing.MiniTablePoints)
            .ThenByDescending(standing => standing.MiniScoreDifference)
            .ThenByDescending(standing => usesSetScores ? standing.MiniSubscoreDifference : 0)
            .ThenByDescending(standing => standing.ScoreDifference)
            .ThenByDescending(standing => usesSetScores ? standing.SubscoreDifference : 0)
            .ThenByDescending(standing => standing.ScoreFor)
            .ThenByDescending(standing => usesSetScores ? standing.SubscoreFor : 0)
            .ThenBy(standing => standing.Seed)
            .ThenBy(standing => standing.TeamId)
            .ToList();

        return new GroupStandingTable(group.Id, group.Name, usesSetScores, ordered.Select((standing, index) =>
            new GroupStandingRow(index + 1, standing.TeamId, standing.TeamName, standing.Seed,
                standing.Played, standing.Wins, standing.Draws, standing.Losses, standing.TablePoints,
                standing.ScoreFor, standing.ScoreAgainst, standing.SubscoreFor, standing.SubscoreAgainst)).ToList());
    }

    private static void ApplyMatch(MutableStanding home, MutableStanding away, Match match,
        int pointsForWin, int pointsForDraw, int pointsForLoss, StandingScope scope)
    {
        var homeScore = match.HomeScore!.Value;
        var awayScore = match.AwayScore!.Value;
        var homeSubscore = match.SetScores.Sum(set => set.HomeScore);
        var awaySubscore = match.SetScores.Sum(set => set.AwayScore);
        var outcome = MatchOutcomeResolver.Resolve(match);
        var homePoints = outcome == MatchOutcome.Draw
            ? pointsForDraw
            : outcome == MatchOutcome.HomeWin ? pointsForWin : pointsForLoss;
        var awayPoints = outcome == MatchOutcome.Draw
            ? pointsForDraw
            : outcome == MatchOutcome.AwayWin ? pointsForWin : pointsForLoss;

        if (scope == StandingScope.MiniTable)
        {
            home.MiniTablePoints += homePoints;
            away.MiniTablePoints += awayPoints;
            home.MiniScoreFor += homeScore;
            home.MiniScoreAgainst += awayScore;
            away.MiniScoreFor += awayScore;
            away.MiniScoreAgainst += homeScore;
            home.MiniSubscoreFor += homeSubscore;
            home.MiniSubscoreAgainst += awaySubscore;
            away.MiniSubscoreFor += awaySubscore;
            away.MiniSubscoreAgainst += homeSubscore;
            return;
        }

        home.Played++;
        away.Played++;
        home.TablePoints += homePoints;
        away.TablePoints += awayPoints;
        AddScores(home, homeScore, awayScore, homeSubscore, awaySubscore);
        AddScores(away, awayScore, homeScore, awaySubscore, homeSubscore);
        if (outcome == MatchOutcome.Draw)
        {
            home.Draws++;
            away.Draws++;
        }
        else if (outcome == MatchOutcome.HomeWin)
        {
            home.Wins++;
            away.Losses++;
        }
        else
        {
            away.Wins++;
            home.Losses++;
        }
    }

    private static void AddScores(MutableStanding standing, int scoreFor, int scoreAgainst,
        int subscoreFor, int subscoreAgainst)
    {
        standing.ScoreFor += scoreFor;
        standing.ScoreAgainst += scoreAgainst;
        standing.SubscoreFor += subscoreFor;
        standing.SubscoreAgainst += subscoreAgainst;
    }

    private static Dictionary<long, MatchAggregate> BuildMatchAggregates(
        IEnumerable<long> teamIds, IEnumerable<Match> matches)
    {
        var result = teamIds.ToDictionary(teamId => teamId, _ => new MatchAggregate());
        foreach (var match in matches)
        {
            var home = result[match.HomeTeamId!.Value];
            var away = result[match.AwayTeamId!.Value];
            var homeSubscore = match.SetScores.Sum(set => set.HomeScore);
            var awaySubscore = match.SetScores.Sum(set => set.AwayScore);
            home.Add(match.HomeScore!.Value, match.AwayScore!.Value, homeSubscore, awaySubscore);
            away.Add(match.AwayScore!.Value, match.HomeScore!.Value, awaySubscore, homeSubscore);
        }
        return result;
    }

    private static IEnumerable<GroupStandingRow> OrderGroupRowsForCrossGroupPlacement(
        IEnumerable<GroupStandingRow> rows,
        bool usesSetScores) =>
        rows.OrderByDescending(row => row.TablePoints)
            .ThenByDescending(row => row.ScoreDifference)
            .ThenByDescending(row => usesSetScores ? row.SubscoreDifference : 0)
            .ThenByDescending(row => row.ScoreFor)
            .ThenByDescending(row => usesSetScores ? row.SubscoreFor : 0)
            .ThenBy(row => row.Seed)
            .ThenBy(row => row.TeamId);

    private static void AddClassificationRow(List<FinalStandingRow> rows, HashSet<long> placedTeamIds,
        IReadOnlyDictionary<long, GroupStandingRow> groupRows, CompetitionDiscipline discipline,
        Match match, long teamId, int position)
    {
        if (!placedTeamIds.Add(teamId))
        {
            return;
        }

        var team = discipline.Teams.Single(item => item.Id == teamId);
        var groupRow = groupRows.GetValueOrDefault(teamId);
        var aggregate = BuildMatchAggregates(discipline.Teams.Select(item => item.Id), [match])[teamId];
        rows.Add(new FinalStandingRow(position, teamId, TeamName(team), team.Seed,
            match.Name, groupRow?.ScoreFor ?? aggregate.ScoreFor, groupRow?.ScoreAgainst ?? aggregate.ScoreAgainst,
            groupRow?.SubscoreFor ?? aggregate.SubscoreFor, groupRow?.SubscoreAgainst ?? aggregate.SubscoreAgainst));
    }

    private static long? ResolveGroupSourceTeamId(long? groupId, int? rank,
        IReadOnlyDictionary<long, GroupStandingTable> tables) =>
        groupId is not null && rank is not null && tables.TryGetValue(groupId.Value, out var table)
            ? table.Rows.SingleOrDefault(row => row.Position == rank.Value)?.TeamId
            : null;

    private static FinalStandingRow ToFinalRow(GroupStandingRow row, string decidedBy, int? position = null) =>
        new(position ?? row.Position, row.TeamId, row.TeamName, row.Seed, decidedBy,
            row.ScoreFor, row.ScoreAgainst, row.SubscoreFor, row.SubscoreAgainst);

    private static FinalStandingRow ToKnockoutRow(int position, DisciplineTeam team,
        MatchAggregate aggregate, string stageName) => new(position, team.Id, TeamName(team), team.Seed,
        position == 1 ? "vítěz" : $"vyřazení: {stageName}", aggregate.ScoreFor, aggregate.ScoreAgainst,
        aggregate.SubscoreFor, aggregate.SubscoreAgainst);

    private static bool IsGroupComplete(PhaseGroup group) =>
        group.Matches.Count > 0 && group.Matches.All(IsCompletedMatch);

    private static bool IsCompletedMatch(Match match) =>
        match.Status == MatchStatus.Completed && match.HomeTeamId is not null && match.AwayTeamId is not null &&
        match.HomeScore is not null && match.AwayScore is not null;

    private static long? WinnerId(Match match) => MatchOutcomeResolver.Resolve(match) switch
    {
        MatchOutcome.HomeWin => match.HomeTeamId,
        MatchOutcome.AwayWin => match.AwayTeamId,
        _ => null
    };

    private static long? LoserId(Match match) => MatchOutcomeResolver.Resolve(match) switch
    {
        MatchOutcome.HomeWin => match.AwayTeamId,
        MatchOutcome.AwayWin => match.HomeTeamId,
        _ => null
    };

    private static string TeamName(DisciplineTeam team) => string.Join("/", team.Members
        .OrderBy(member => member.Order)
        .Select(member => member.CompetitionEntry.Competitor.LastName));

    private enum StandingScope { Overall, MiniTable }

    private sealed class MutableStanding(long teamId, string teamName, int seed)
    {
        public long TeamId { get; } = teamId;
        public string TeamName { get; } = teamName;
        public int Seed { get; } = seed;
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int TablePoints { get; set; }
        public int MiniTablePoints { get; set; }
        public int ScoreFor { get; set; }
        public int ScoreAgainst { get; set; }
        public int SubscoreFor { get; set; }
        public int SubscoreAgainst { get; set; }
        public int MiniScoreFor { get; set; }
        public int MiniScoreAgainst { get; set; }
        public int MiniSubscoreFor { get; set; }
        public int MiniSubscoreAgainst { get; set; }
        public int ScoreDifference => ScoreFor - ScoreAgainst;
        public int SubscoreDifference => SubscoreFor - SubscoreAgainst;
        public int MiniScoreDifference => MiniScoreFor - MiniScoreAgainst;
        public int MiniSubscoreDifference => MiniSubscoreFor - MiniSubscoreAgainst;
        public double ScoreRatio => StandingRatios.Calculate(ScoreFor, ScoreAgainst);
    }

    private sealed class MatchAggregate
    {
        public int ScoreFor { get; private set; }
        public int ScoreAgainst { get; private set; }
        public int SubscoreFor { get; private set; }
        public int SubscoreAgainst { get; private set; }
        public int ScoreDifference => ScoreFor - ScoreAgainst;
        public int SubscoreDifference => SubscoreFor - SubscoreAgainst;
        public double ScoreRatio => StandingRatios.Calculate(ScoreFor, ScoreAgainst);
        public double SubscoreRatio => StandingRatios.Calculate(SubscoreFor, SubscoreAgainst);

        public void Add(int scoreFor, int scoreAgainst, int subscoreFor, int subscoreAgainst)
        {
            ScoreFor += scoreFor;
            ScoreAgainst += scoreAgainst;
            SubscoreFor += subscoreFor;
            SubscoreAgainst += subscoreAgainst;
        }
    }
}
