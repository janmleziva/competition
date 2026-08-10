using Competition.Models;

namespace Competition.Services;

public interface ICompetitorAdministrationService
{
    Task<IReadOnlyList<CompetitorSummary>> SearchAsync(string? search, CancellationToken cancellationToken = default);
    Task<CompetitorDetails?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(CompetitorInput input, bool allowDuplicateName = false, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(long id, CompetitorInput input, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task<EditionRegistrationDetails?> GetEditionRegistrationAsync(long editionId, CancellationToken cancellationToken = default);
    Task RegisterAsync(long editionId, long competitorId, int seed, CancellationToken cancellationToken = default);
    Task<long> CreateAndRegisterAsync(long editionId, CompetitorInput input, bool allowDuplicateName = false, CancellationToken cancellationToken = default);
    Task<SeedUpdateResult?> UpdateSeedAsync(long editionId, long entryId, int seed, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long editionId, long entryId, CancellationToken cancellationToken = default);
}

public sealed class DuplicateCompetitorException()
    : System.ComponentModel.DataAnnotations.ValidationException(
        "Soutěžící se stejným jménem a příjmením už existuje.");

public sealed record CompetitorSummary(
    long Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    int EditionCount);

public sealed record CompetitorDetails(
    long Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth);

public sealed record CompetitionEntrySummary(
    long Id,
    long CompetitorId,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    int Seed);

public sealed record SeedUpdateResult(bool WasSwapped, string? SwappedCompetitorName);

public sealed record EditionRegistrationDetails(
    long EditionId,
    string EditionName,
    IReadOnlyList<CompetitionEntrySummary> Entries,
    IReadOnlyList<CompetitorSummary> AvailableCompetitors);
