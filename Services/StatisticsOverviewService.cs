using Competition.Data;
using Competition.Domain;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class StatisticsOverviewService(CompetitionDbContext dbContext) : IStatisticsOverviewService
{
    public async Task<IndividualStatisticsOverview> GetIndividualsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = BuildIndividuals(await LoadResultsAsync(null, null, cancellationToken));
        return new IndividualStatisticsOverview(
            RankByPlacements(rows),
            RankByPoints(rows));
    }

    public async Task<EditionIndividualStatistics?> GetEditionAsync(
        long editionId,
        CancellationToken cancellationToken = default)
    {
        var edition = await dbContext.CompetitionEditions
            .AsNoTracking()
            .Where(item => item.Id == editionId)
            .Select(item => new { item.Id, item.Name, item.StartDate })
            .SingleOrDefaultAsync(cancellationToken);
        if (edition is null)
        {
            return null;
        }

        var rows = BuildIndividuals(await LoadResultsAsync(editionId, null, cancellationToken));
        return new EditionIndividualStatistics(
            edition.Id,
            edition.Name,
            edition.StartDate,
            RankByPlacements(rows),
            RankByPoints(rows));
    }

    public async Task<EditionCompetitorResults?> GetEditionCompetitorResultsAsync(
        long editionId,
        long competitorId,
        CancellationToken cancellationToken = default)
    {
        var participant = await dbContext.CompetitionEntries
            .AsNoTracking()
            .Where(entry => entry.CompetitionEditionId == editionId && entry.CompetitorId == competitorId)
            .Select(entry => new
            {
                EditionId = entry.CompetitionEditionId,
                EditionName = entry.CompetitionEdition.Name,
                CompetitorId = entry.CompetitorId,
                entry.Competitor.FirstName,
                entry.Competitor.LastName
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (participant is null)
        {
            return null;
        }

        var matches = await dbContext.Matches
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(match => match.DisciplinePhase)
                .ThenInclude(phase => phase.CompetitionDiscipline)
                .ThenInclude(discipline => discipline.Discipline)
            .Include(match => match.PhaseGroup)
            .Include(match => match.HomeTeam!).ThenInclude(team => team.Members)
                .ThenInclude(member => member.CompetitionEntry).ThenInclude(entry => entry.Competitor)
            .Include(match => match.AwayTeam!).ThenInclude(team => team.Members)
                .ThenInclude(member => member.CompetitionEntry).ThenInclude(entry => entry.Competitor)
            .Include(match => match.SetScores)
            .Where(match =>
                match.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId &&
                match.Status == MatchStatus.Completed &&
                match.HomeScore != null && match.AwayScore != null &&
                match.HomeTeam != null && match.AwayTeam != null &&
                (match.HomeTeam.Members.Any(member => member.CompetitionEntry.CompetitorId == competitorId) ||
                 match.AwayTeam.Members.Any(member => member.CompetitionEntry.CompetitorId == competitorId)))
            .OrderBy(match => match.DisciplinePhase.CompetitionDiscipline.Order)
            .ThenBy(match => match.DisciplinePhase.Order)
            .ThenBy(match => match.PhaseGroup == null ? 0 : match.PhaseGroup.Order)
            .ThenBy(match => match.Order)
            .ToListAsync(cancellationToken);

        var disciplineResults = matches
            .GroupBy(match => new
            {
                Id = match.DisciplinePhase.CompetitionDiscipline.DisciplineId,
                Name = match.DisciplinePhase.CompetitionDiscipline.Discipline.Name,
                Order = match.DisciplinePhase.CompetitionDiscipline.Order
            })
            .OrderBy(group => group.Key.Order)
            .Select(group => new EditionCompetitorDisciplineResults(
                group.Key.Id,
                group.Key.Name,
                group.Select(match => BuildMatchResult(match, competitorId)).ToList()))
            .ToList();

        return new EditionCompetitorResults(
            participant.EditionId,
            participant.EditionName,
            participant.CompetitorId,
            participant.FirstName,
            participant.LastName,
            disciplineResults);
    }

    public async Task<DisciplineStatisticsOverview> GetDisciplinesAsync(
        long? disciplineId = null,
        CancellationToken cancellationToken = default)
    {
        var disciplines = await dbContext.Disciplines
            .AsNoTracking()
            .Where(item => item.CompetitionDisciplines.Any(discipline => discipline.FinalStandings.Any()))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new DisciplineStatisticsOption(item.Id, item.Name))
            .ToListAsync(cancellationToken);

        var selected = disciplineId.HasValue
            ? disciplines.SingleOrDefault(item => item.Id == disciplineId.Value)
            : disciplines.FirstOrDefault();
        if (selected is null)
        {
            return new DisciplineStatisticsOverview(disciplines, null, null, [], []);
        }

        var rows = BuildIndividuals(await LoadResultsAsync(null, selected.Id, cancellationToken));
        var rankings = RankByPlacements(rows);
        return new DisciplineStatisticsOverview(
            disciplines,
            selected.Id,
            selected.Name,
            rankings,
            BuildRecords(rankings));
    }

    private async Task<List<ResultRow>> LoadResultsAsync(
        long? editionId,
        long? disciplineId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DisciplineStandings
            .AsNoTracking()
            .Where(standing =>
                (!editionId.HasValue || standing.CompetitionDiscipline.CompetitionEditionId == editionId.Value) &&
                (!disciplineId.HasValue || standing.CompetitionDiscipline.DisciplineId == disciplineId.Value));

        return await query
            .SelectMany(
                standing => standing.DisciplineTeam.Members,
                (standing, member) => new ResultRow(
                    member.CompetitionEntry.CompetitorId,
                    member.CompetitionEntry.Competitor.FirstName,
                    member.CompetitionEntry.Competitor.LastName,
                    standing.CompetitionDiscipline.CompetitionEditionId,
                    standing.Rank,
                    standing.PointsAwarded + standing.DisciplineTeam.BonusAwards
                        .Where(award => award.CompetitionDisciplineId == standing.CompetitionDisciplineId)
                        .Sum(award => award.PointsAwarded)))
            .ToListAsync(cancellationToken);
    }

    private static List<IndividualRow> BuildIndividuals(IEnumerable<ResultRow> results) => results
        .GroupBy(row => new { row.CompetitorId, row.FirstName, row.LastName })
        .Select(group => new IndividualRow(
            group.Key.CompetitorId,
            group.Key.FirstName,
            group.Key.LastName,
            group.Select(row => row.EditionId).Distinct().Count(),
            group.Count(),
            group.Sum(row => row.Points),
            PlacementSummary.FromRanks(group.Select(row => row.Rank))))
        .ToList();

    private static IReadOnlyList<RankedIndividualStatistics> RankByPlacements(IEnumerable<IndividualRow> source)
    {
        var ordered = source
            .Order(Comparer<IndividualRow>.Create((left, right) =>
                PlacementSummaryOrdering.Compare(left.Placements, right.Placements)))
            .ThenByDescending(row => row.TotalPoints)
            .ThenBy(row => row.LastName)
            .ThenBy(row => row.FirstName)
            .ThenBy(row => row.CompetitorId)
            .ToList();

        return AssignPlaces(
            ordered,
            (left, right) => PlacementSummaryOrdering.AreEqual(left.Placements, right.Placements));
    }

    private static IReadOnlyList<RankedIndividualStatistics> RankByPoints(IEnumerable<IndividualRow> source)
    {
        var ordered = source
            .OrderByDescending(row => row.TotalPoints)
            .ThenBy(row => row, Comparer<IndividualRow>.Create((left, right) =>
                PlacementSummaryOrdering.Compare(left.Placements, right.Placements)))
            .ThenBy(row => row.LastName)
            .ThenBy(row => row.FirstName)
            .ThenBy(row => row.CompetitorId)
            .ToList();

        return AssignPlaces(ordered, (left, right) => left.TotalPoints == right.TotalPoints);
    }

    private static IReadOnlyList<RankedIndividualStatistics> AssignPlaces(
        IReadOnlyList<IndividualRow> rows,
        Func<IndividualRow, IndividualRow, bool> sharesPlace)
    {
        var result = new List<RankedIndividualStatistics>(rows.Count);
        var place = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            if (index == 0 || !sharesPlace(rows[index - 1], rows[index]))
            {
                place = index + 1;
            }

            var row = rows[index];
            result.Add(new RankedIndividualStatistics(
                place,
                row.CompetitorId,
                row.FirstName,
                row.LastName,
                row.EditionCount,
                row.ResultCount,
                row.TotalPoints,
                row.Placements));
        }

        return result;
    }

    private static IReadOnlyList<DisciplineStatisticsRecord> BuildRecords(
        IReadOnlyList<RankedIndividualStatistics> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        return
        [
            CreateRecord("Nejvíce vítězství", rows.Max(row => row.Placements.CountAt(1)), "vítězství", rows,
                row => row.Placements.CountAt(1)),
            CreateRecord("Nejvíce umístění na stupních", rows.Max(row =>
                row.Placements.CountAt(1) + row.Placements.CountAt(2) + row.Placements.CountAt(3)), "umístění", rows,
                row => row.Placements.CountAt(1) + row.Placements.CountAt(2) + row.Placements.CountAt(3)),
            CreateRecord("Nejvíce účastí", rows.Max(row => row.ResultCount), "účastí", rows,
                row => row.ResultCount),
            CreateRecord("Nejvíce bodů", rows.Max(row => row.TotalPoints), "bodů", rows,
                row => row.TotalPoints)
        ];
    }

    private static DisciplineStatisticsRecord CreateRecord(
        string label,
        int value,
        string unit,
        IReadOnlyList<RankedIndividualStatistics> rows,
        Func<RankedIndividualStatistics, int> selector) =>
        new(label, value, unit, rows.Where(row => selector(row) == value).ToList());

    private static EditionCompetitorMatchResult BuildMatchResult(Match match, long competitorId)
    {
        var isHome = match.HomeTeam!.Members.Any(member => member.CompetitionEntry.CompetitorId == competitorId);
        var ownTeam = isHome ? match.HomeTeam : match.AwayTeam!;
        var opponentTeam = isHome ? match.AwayTeam! : match.HomeTeam;
        var scoreFor = isHome ? match.HomeScore!.Value : match.AwayScore!.Value;
        var scoreAgainst = isHome ? match.AwayScore!.Value : match.HomeScore!.Value;
        var outcome = scoreFor > scoreAgainst ? "Výhra" : scoreFor < scoreAgainst ? "Prohra" : "Remíza";
        var teammates = FormatPeople(ownTeam.Members
            .Where(member => member.CompetitionEntry.CompetitorId != competitorId));
        var opponent = FormatPeople(opponentTeam.Members);
        var subscores = match.SetScores
            .OrderBy(set => set.SetNumber)
            .Select(set => isHome ? $"{set.HomeScore}:{set.AwayScore}" : $"{set.AwayScore}:{set.HomeScore}")
            .ToList();
        var (displayName, stageLabel) = FormatMatchStage(match);

        return new EditionCompetitorMatchResult(
            match.Id,
            match.DisciplinePhase.Name,
            match.PhaseGroup?.Name,
            match.Name,
            displayName,
            stageLabel,
            teammates,
            opponent,
            outcome,
            scoreFor,
            scoreAgainst,
            subscores);
    }

    private static (string DisplayName, string StageLabel) FormatMatchStage(Match match)
    {
        if (match.DisciplinePhase.Type == PhaseType.Group && match.PhaseGroup is not null)
        {
            var displayName = string.Equals(match.PhaseGroup.Name, "Detaily", StringComparison.OrdinalIgnoreCase)
                ? match.DisciplinePhase.Name
                : match.PhaseGroup.Name;
            var roundMarker = ". kolo";
            var roundEnd = match.Name.IndexOf(roundMarker, StringComparison.OrdinalIgnoreCase);
            if (roundEnd >= 0)
            {
                var roundStart = match.Name.LastIndexOf(' ', roundEnd);
                var round = match.Name[(roundStart < 0 ? 0 : roundStart + 1)..(roundEnd + roundMarker.Length)];
                return (displayName, round);
            }

            return (displayName, match.DisciplinePhase.Name);
        }

        var context = new[] { match.PhaseGroup?.Name, match.DisciplinePhase.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
        return (match.Name, string.Join(" · ", context));
    }

    private static string FormatPeople(IEnumerable<DisciplineTeamMember> members)
    {
        var names = members
            .OrderBy(member => member.Order)
            .Select(member => $"{member.CompetitionEntry.Competitor.FirstName} {member.CompetitionEntry.Competitor.LastName}")
            .ToList();
        return names.Count == 0 ? "—" : string.Join(" / ", names);
    }

    private sealed record ResultRow(
        long CompetitorId,
        string FirstName,
        string LastName,
        long EditionId,
        int Rank,
        int Points);

    private sealed record IndividualRow(
        long CompetitorId,
        string FirstName,
        string LastName,
        int EditionCount,
        int ResultCount,
        int TotalPoints,
        PlacementSummary Placements);
}
