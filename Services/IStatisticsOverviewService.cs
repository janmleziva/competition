namespace Competition.Services;

public interface IStatisticsOverviewService
{
    Task<IndividualStatisticsOverview> GetIndividualsAsync(CancellationToken cancellationToken = default);
    Task<EditionIndividualStatistics?> GetEditionAsync(long editionId, CancellationToken cancellationToken = default);
    Task<EditionCompetitorResults?> GetEditionCompetitorResultsAsync(long editionId, long competitorId, CancellationToken cancellationToken = default);
    Task<DisciplineTeamResults?> GetDisciplineTeamResultsAsync(long editionId, long competitionDisciplineId, long teamId, CancellationToken cancellationToken = default);
    Task<DisciplineStatisticsOverview> GetDisciplinesAsync(long? disciplineId = null, CancellationToken cancellationToken = default);
}

public sealed record RankedIndividualStatistics(
    int Place,
    long CompetitorId,
    string FirstName,
    string LastName,
    int EditionCount,
    int ResultCount,
    int TotalPoints,
    PlacementSummary Placements);

public enum IndividualRankingTableMode
{
    Placements,
    Points
}

public sealed record IndividualRankingTableModel(
    IReadOnlyList<RankedIndividualStatistics> Rows,
    IndividualRankingTableMode Mode,
    long? EditionId = null);

public sealed record IndividualStatisticsOverview(
    IReadOnlyList<RankedIndividualStatistics> ByPlacements,
    IReadOnlyList<RankedIndividualStatistics> ByPoints);

public sealed record EditionIndividualStatistics(
    long EditionId,
    string EditionName,
    DateOnly StartDate,
    IReadOnlyList<RankedIndividualStatistics> ByPlacements,
    IReadOnlyList<RankedIndividualStatistics> ByPoints);

public sealed record EditionCompetitorMatchResult(
    long MatchId,
    string PhaseName,
    string? GroupName,
    string MatchName,
    string DisplayName,
    string StageLabel,
    string Teammates,
    string Opponent,
    string Outcome,
    int ScoreFor,
    int ScoreAgainst,
    IReadOnlyList<string> Subscores);

public sealed record EditionCompetitorDisciplineResults(
    long DisciplineId,
    string DisciplineName,
    IReadOnlyList<EditionCompetitorMatchResult> Matches);

public sealed record EditionCompetitorResults(
    long EditionId,
    string EditionName,
    long CompetitorId,
    string FirstName,
    string LastName,
    IReadOnlyList<EditionCompetitorDisciplineResults> Disciplines);

public sealed record DisciplineTeamMatchResult(
    long MatchId,
    string DisplayName,
    string StageLabel,
    long? OpponentTeamId,
    string OpponentTeamName,
    string Outcome,
    int? ScoreFor,
    int? ScoreAgainst,
    IReadOnlyList<string> Subscores);

public sealed record DisciplineTeamResults(
    long EditionId,
    string EditionName,
    long CompetitionDisciplineId,
    string DisciplineName,
    long TeamId,
    string TeamName,
    IReadOnlyList<DisciplineTeamMatchResult> Matches);

public sealed record DisciplineStatisticsOption(long Id, string Name);

public sealed record DisciplineStatisticsRecord(
    string Label,
    int Value,
    string Unit,
    IReadOnlyList<RankedIndividualStatistics> Holders);

public sealed record DisciplineStatisticsOverview(
    IReadOnlyList<DisciplineStatisticsOption> Disciplines,
    long? SelectedDisciplineId,
    string? SelectedDisciplineName,
    IReadOnlyList<RankedIndividualStatistics> Rankings,
    IReadOnlyList<DisciplineStatisticsRecord> Records);
