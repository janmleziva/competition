namespace Competition.Services;

public interface ICompetitorStatisticsService
{
    Task<CompetitorStatistics?> GetAsync(long competitorId, CancellationToken cancellationToken = default);
}

public sealed record CompetitorStatisticsCell(int Rank, int Points, string TeamName);

public sealed record PlacementSummary(IReadOnlyDictionary<int, int> Counts)
{
    public int TotalPlacements => Counts.Values.Sum();

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
