namespace Competition.Services;

public interface ICompetitionScoringService
{
    Task<DisciplineScoringSetup?> GetDisciplineSetupAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> SetPointSystemAsync(
        long editionId, long competitionDisciplineId, long? pointSystemId, CancellationToken cancellationToken = default);
    Task<bool> FinalizeDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> CloseDisciplineWithAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> ReopenDisciplineAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAwardedPointsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<EditionOverallStanding?> GetEditionOverallStandingAsync(
        long editionId, CancellationToken cancellationToken = default);
}

public sealed record DisciplineAwardedStanding(int Rank, long TeamId, string TeamName, int Points);

public sealed record DisciplineScoringSetup(
    long EditionId,
    long DisciplineId,
    bool IsClosed,
    bool CanFinalize,
    string? FinalizeBlockReason,
    long? AwardPointSystemId,
    string? AwardPointSystemName,
    IReadOnlyList<AwardPointSystemItem> AvailableSystems,
    IReadOnlyList<DisciplineAwardedStanding> FinalStandings);

public sealed record EditionStandingDiscipline(long Id, string Name, bool IsClosed);

public sealed record EditionStandingCell(
    long DisciplineId,
    string TeamName,
    int Rank,
    int Points);

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
