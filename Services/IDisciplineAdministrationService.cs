using Competition.Domain;
using Competition.Models;

namespace Competition.Services;

public interface IDisciplineAdministrationService
{
    Task<IReadOnlyList<DisciplineCatalogItem>> ListCatalogAsync(CancellationToken cancellationToken = default);
    Task<long> CreateCatalogAsync(DisciplineCatalogInput input, CancellationToken cancellationToken = default);
    Task<bool> RenameCatalogAsync(long id, DisciplineCatalogInput input, CancellationToken cancellationToken = default);
    Task<bool> DeleteCatalogAsync(long id, CancellationToken cancellationToken = default);
    Task<EditionDisciplineSetup?> GetEditionSetupAsync(long editionId, CancellationToken cancellationToken = default);
    Task<EditionDisciplineDetail?> GetDetailAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<DisciplineParticipantSetup?> GetParticipantSetupAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<long> AttachAsync(long editionId, EditionDisciplineInput input, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(long editionId, long competitionDisciplineId, EditionDisciplineInput input, CancellationToken cancellationToken = default);
    Task<bool> SetLockAsync(long editionId, long competitionDisciplineId, bool isLocked, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> AssignAllAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> UpdateParticipantsAsync(long editionId, long competitionDisciplineId, IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);
    Task<bool> CreateTeamAsync(long editionId, long competitionDisciplineId, IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default);
    Task<bool> RandomizeTeamsAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> DeleteTeamAsync(long editionId, long competitionDisciplineId, long teamId, CancellationToken cancellationToken = default);
}

public sealed record DisciplineCatalogItem(long Id, string Name, bool IsInUse);
public sealed record DisciplineParticipant(long EntryId, int Seed, string FirstName, string LastName, bool IsAssigned, bool IsInTeam);
public sealed record DisciplineTeamMemberItem(long EntryId, int Seed, string FirstName, string LastName, int Order);
public sealed record DisciplineTeamItem(long TeamId, int Seed, IReadOnlyList<DisciplineTeamMemberItem> Members);
public sealed record ConfiguredDisciplineItem(
    long Id, long DisciplineId, string Name, int Order, PlayingSystemType PlayingSystem,
    int TeamSize, DateTime? ScheduledAt, bool UsesSetScores, int TeamCount, int ParticipantCount,
    string? Description = null, int? SetsToWin = null, bool IsLocked = false, bool HasResults = false,
    bool IsScheduleLocked = false, bool HasMatches = false, bool HasPhases = false, bool HasPhaseAssignments = false,
    bool IsClosed = false, long? AwardPointSystemId = null, string? AwardPointSystemName = null,
    IReadOnlyList<DisciplineAwardedStanding>? FinalStandings = null, bool AreAllMatchesCompleted = false,
    IReadOnlyList<AwardPointRuleItem>? AwardPointRules = null)
{
    public bool IsPhaseSetupAvailable =>
        ParticipantCount > 0 && TeamCount * TeamSize == ParticipantCount;
    public bool CanRemove => !IsClosed && !IsScheduleLocked && !HasResults;
    public string? RemoveBlockReason => IsClosed
        ? "Uzavřenou disciplínu nelze odebrat."
        : HasResults
        ? "Disciplínu nelze odebrat, protože obsahuje výsledky."
        : IsScheduleLocked
            ? "Disciplínu nelze odebrat, protože její rozpis je uzamčený."
            : null;
    public bool CanCreateSchedule => IsPhaseSetupAvailable && !HasPhases && !HasMatches;
    public bool CanFinalize => !IsClosed && FinalStandings is not { Count: > 0 } &&
        AwardPointSystemId is not null && HasMatches && AreAllMatchesCompleted;
}
public sealed record EditionDisciplineSetup(
    long EditionId, string EditionName, IReadOnlyList<DisciplineCatalogItem> AvailableDisciplines,
    IReadOnlyList<ConfiguredDisciplineItem> Disciplines);
public sealed record EditionDisciplineDetail(long EditionId, string EditionName, ConfiguredDisciplineItem Discipline);
public sealed record DisciplineParticipantSetup(
    long EditionId,
    string EditionName,
    ConfiguredDisciplineItem Discipline,
    IReadOnlyList<DisciplineParticipant> Participants,
    IReadOnlyList<DisciplineTeamItem> Teams,
    IReadOnlyList<DisciplineParticipant> AvailableForTeams);
