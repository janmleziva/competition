namespace Competition.Services;

public interface ICompetitorStatisticsService
{
    Task<CompetitorStatistics?> GetAsync(long competitorId, CancellationToken cancellationToken = default);
}

public sealed record CompetitorStatisticsCell(int Rank, int Points, string TeamName);

public sealed record PlacementSummary(IReadOnlyDictionary<int, int> Counts)
{
    public int TotalPlacements => Counts.Values.Sum();

    public int CountAt(int rank) => Counts.GetValueOrDefault(rank);

    public static PlacementSummary FromRanks(IEnumerable<int> ranks) => new(
        ranks
            .GroupBy(rank => rank)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count()));

    public string Format()
    {
        if (Counts.Count == 0)
        {
            return "-";
        }

        return string.Join(", ", Counts
            .OrderBy(item => item.Key)
            .Select(item => $"{item.Key}. místo {item.Value}x"));
    }
}

public static class PlacementSummaryOrdering
{
    public static int Compare(PlacementSummary left, PlacementSummary right)
    {
        var maxRank = Math.Max(
            left.Counts.Count == 0 ? 0 : left.Counts.Keys.Max(),
            right.Counts.Count == 0 ? 0 : right.Counts.Keys.Max());

        for (var rank = 1; rank <= maxRank; rank++)
        {
            var comparison = right.CountAt(rank).CompareTo(left.CountAt(rank));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return right.TotalPlacements.CompareTo(left.TotalPlacements);
    }

    public static bool AreEqual(PlacementSummary left, PlacementSummary right) =>
        Compare(left, right) == 0;
}

public sealed record CompetitorEditionStatistics(
    long EditionId,
    string EditionName,
    DateOnly StartDate,
    int OverallPlace,
    int TotalPoints,
    PlacementSummary Placements,
    IReadOnlyDictionary<string, CompetitorStatisticsCell> Disciplines);

public sealed record CompetitorDisciplineStatistics(
    string DisciplineName,
    int TotalPoints,
    PlacementSummary Placements,
    IReadOnlyDictionary<long, CompetitorStatisticsCell> Editions);

public sealed record CompetitorTeamMemberStatistics(
    long CompetitorId,
    string FirstName,
    string LastName,
    int TotalPoints,
    PlacementSummary Placements,
    IReadOnlyDictionary<string, PlacementSummary> Disciplines);

public sealed record CompetitorStatistics(
    long CompetitorId,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<CompetitorEditionStatistics> Editions,
    int TotalPoints,
    IReadOnlyDictionary<string, int> TotalsByDiscipline,
    PlacementSummary Placements,
    IReadOnlyDictionary<string, PlacementSummary> PlacementsByDiscipline,
    IReadOnlyList<CompetitorDisciplineStatistics> ByDiscipline,
    IReadOnlyList<CompetitorTeamMemberStatistics> ByTeamMember);
