namespace Competition.Services;

public interface ICompetitorStatisticsService
{
    Task<CompetitorStatistics?> GetAsync(long competitorId, CancellationToken cancellationToken = default);
}

public sealed record CompetitorStatisticsCell(int Rank, int Points, string TeamName);

public sealed record CompetitorEditionStatistics(
    long EditionId,
    string EditionName,
    DateOnly StartDate,
    int OverallPlace,
    int TotalPoints,
    IReadOnlyDictionary<string, CompetitorStatisticsCell> Disciplines);

public sealed record CompetitorDisciplineStatistics(
    string DisciplineName,
    int TotalPoints,
    IReadOnlyDictionary<long, CompetitorStatisticsCell> Editions);

public sealed record CompetitorTeamMemberStatistics(
    long CompetitorId,
    string FirstName,
    string LastName,
    int TotalPoints,
    IReadOnlyDictionary<string, int> Disciplines);

public sealed record CompetitorStatistics(
    long CompetitorId,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<CompetitorEditionStatistics> Editions,
    int TotalPoints,
    IReadOnlyDictionary<string, int> TotalsByDiscipline,
    IReadOnlyList<CompetitorDisciplineStatistics> ByDiscipline,
    IReadOnlyList<CompetitorTeamMemberStatistics> ByTeamMember);
