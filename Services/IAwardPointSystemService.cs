using Competition.Models;

namespace Competition.Services;

public interface IAwardPointSystemService
{
    Task<IReadOnlyList<AwardPointSystemItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<long> CreateAsync(AwardPointSystemInput input, CancellationToken cancellationToken = default);
    Task<long?> CreateCopyAsync(long sourceId, AwardPointSystemInput input, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(long id, AwardPointSystemInput input, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record AwardPointRuleItem(int Rank, int Points);

public sealed record AwardPointSystemItem(
    long Id,
    string Name,
    IReadOnlyList<AwardPointRuleItem> Rules,
    bool IsInUse);
