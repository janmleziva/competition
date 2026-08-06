using Competition.Models;

namespace Competition.Services;

public interface IEditionAdministrationService
{
    Task<IReadOnlyList<EditionSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<EditionDetails?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(EditionInput input, Guid creationToken, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(long id, EditionInput input, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record EditionSummary(
    long Id,
    string Name,
    string City,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);

public sealed record EditionDetails(
    long Id,
    string Name,
    string City,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);
