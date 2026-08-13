namespace Competition.Services;

using Competition.Domain;
using Competition.Models;

public interface ICompetitionScoringService
{
    Task<DisciplineScoringSetup?> GetDisciplineSetupAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> SetPointSystemAsync(
        long editionId, long competitionDisciplineId, long? pointSystemId, CancellationToken cancellationToken = default);
    Task<bool> SetBonusPointRulesAsync(
        long editionId, long competitionDisciplineId, IReadOnlyCollection<BonusPointRuleInput> rules,
        CancellationToken cancellationToken = default);
    Task<bool> FinalizeDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> CloseDisciplineWithAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> ReopenDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<EditionOverallStanding?> GetEditionOverallStandingAsync(
        long editionId, CancellationToken cancellationToken = default, bool includeBonusPoints = true);
}

public sealed record DisciplineAwardedStanding(int Rank, long TeamId, string TeamName, int Points);
public sealed record BonusPointRuleItem(BonusPointType Type, int Points);
public sealed record DisciplineBonusAwardItem(
    BonusPointType Type,
    int Points,
    long TeamId,
    string TeamName,
    int MetricTotal,
    int MatchCount);
public sealed record DisciplineBonusMetricStanding(
    BonusPointType Type,
    int Rank,
    long TeamId,
    string TeamName,
    int MetricTotal,
    int MatchCount);

public sealed record DisciplineScoringSetup(
    long EditionId,
    long DisciplineId,
    bool IsClosed,
    bool CanFinalize,
    string? FinalizeBlockReason,
    long? AwardPointSystemId,
    string? AwardPointSystemName,
    IReadOnlyList<AwardPointSystemItem> AvailableSystems,
    IReadOnlyList<DisciplineAwardedStanding> FinalStandings,
    IReadOnlyList<BonusPointRuleItem>? BonusRules = null,
    IReadOnlyList<DisciplineBonusAwardItem>? BonusAwards = null,
    IReadOnlyList<DisciplineBonusMetricStanding>? BonusMetricStandings = null);

public sealed record EditionStandingDiscipline(long Id, string Name, bool IsClosed);

public sealed record EditionStandingCell(
    long DisciplineId,
    string TeamName,
    int Rank,
    int Points,
    int PlacementPoints = 0,
    int BonusPoints = 0);

public sealed record EditionStandingRow(
    int Place,
    long EntryId,
    string FirstName,
    string LastName,
    IReadOnlyDictionary<long, EditionStandingCell> Disciplines,
    int TotalPoints);

public sealed record EditionOverallStanding(
    long EditionId,
    string EditionName,
    IReadOnlyList<EditionStandingDiscipline> Disciplines,
    IReadOnlyList<EditionStandingRow> Rows);

public static class BonusPointTypeLabels
{
    public static string Get(BonusPointType type) => type switch
    {
        BonusPointType.LowestAverageSubscoreAgainst => "Nejméně bodů soupeře v setech",
        BonusPointType.HighestAverageScoreFor => "Nejvíce získaných bodů",
        BonusPointType.HighestAverageScoreDifference => "Nejvyšší rozdíl skóre na zápas",
        _ => type.ToString()
    };

    public static string FormatPoints(int points) => points switch
    {
        1 => "1 bonusový bod",
        >= 2 and <= 4 => $"{points} bonusové body",
        _ => $"{points} bonusových bodů"
    };

    public static string FormatMetric(int total, int matchCount) => matchCount == 0
        ? "–"
        : ((decimal)total / matchCount).ToString("0.##");
}
