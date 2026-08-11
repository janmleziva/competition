using Competition.Domain;
using Competition.Models;

namespace Competition.Services;

public interface IPhaseSetupService
{
    Task<DisciplinePhaseSetup?> GetSetupAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> SetPlayingSystemAsync(long editionId, long competitionDisciplineId, PlayingSystemType playingSystem, CancellationToken cancellationToken = default);
    Task<long> CreatePhaseAsync(long editionId, long competitionDisciplineId, PhaseInput input, CancellationToken cancellationToken = default);
    Task<long> CreateGroupAsync(long editionId, long competitionDisciplineId, long phaseId, PhaseGroupInput input, CancellationToken cancellationToken = default);
    Task<bool> AssignGroupTeamsAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, IReadOnlyCollection<long> teamIds, CancellationToken cancellationToken = default);
    Task<bool> DeleteGroupAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, CancellationToken cancellationToken = default);
    Task<bool> DeletePhaseAsync(long editionId, long competitionDisciplineId, long phaseId, CancellationToken cancellationToken = default);
    Task<int> RandomlyAssignGroupTeamsAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, int teamCount, CancellationToken cancellationToken = default);
    Task<int> GenerateRoundRobinAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, CancellationToken cancellationToken = default);
    Task<int> GeneratePresetMatchesAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<int> GenerateKnockoutMatchesAsync(long editionId, long competitionDisciplineId, bool randomizeTeams, CancellationToken cancellationToken = default);
    Task<long> CreateMatchSlotAsync(long editionId, long competitionDisciplineId, long phaseId, MatchSlotInput input, CancellationToken cancellationToken = default);
    Task<bool> UpdateKnockoutMatchTeamsAsync(long editionId, long competitionDisciplineId, MatchTeamsInput input, CancellationToken cancellationToken = default);
    Task<bool> ResetScheduleAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> SetScheduleLockAsync(long editionId, long competitionDisciplineId, bool isLocked, CancellationToken cancellationToken = default);
    Task<bool> SetResultsLockAsync(long editionId, long competitionDisciplineId, bool isLocked, CancellationToken cancellationToken = default);
    Task<bool> DeleteResultsAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<int> GenerateRandomResultsAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default);
    Task<bool> UpdateMatchResultAsync(long editionId, long competitionDisciplineId, MatchResultInput input, bool isAdmin, CancellationToken cancellationToken = default);
    Task<bool> UpdateMatchSetScoresAsync(long editionId, long competitionDisciplineId, MatchSetScoresInput input, bool isAdmin, CancellationToken cancellationToken = default);
}

public sealed record PhaseSetupTeam(long Id, int Seed, string Name);
public sealed record PhaseSetupMatchSource(long MatchId, string Name);
public sealed record PhaseSetupSetScore(int SetNumber, int HomeScore, int AwayScore);
public sealed record PhaseSetupMatch(long Id, string Name, int Order, long? HomeTeamId, string? HomeTeamName, long? AwayTeamId, string? AwayTeamName, long? HomeSourceMatchId, long? AwaySourceMatchId, string? HomeSource, string? AwaySource, string? HomeAdvancementSource, string? AwayAdvancementSource, int? HomeScore, int? AwayScore, int Version, string? RoundLabel, IReadOnlyList<PhaseSetupSetScore> SetScores);
public sealed record PhaseSetupGroup(long Id, string Name, int Order, int? Capacity, IReadOnlyList<long> TeamIds, IReadOnlyList<PhaseSetupMatch> Matches);
public sealed record PhaseSetupPhase(long Id, string Name, PhaseType Type, int Order, int PointsForWin, int PointsForDraw, int PointsForLoss, IReadOnlyList<PhaseSetupGroup> Groups, IReadOnlyList<PhaseSetupMatch> Matches);
public sealed record DisciplinePhaseSetup(long EditionId, string EditionName, long DisciplineId, string DisciplineName, PlayingSystemType PlayingSystem, bool UsesSetScores, int? SetsToWin, bool IsScheduleLocked, bool AreResultsLocked, bool IsClosed, bool IsAnonymousResultEditingEnabled, bool HasMatchResults, IReadOnlyList<PhaseSetupTeam> Teams, IReadOnlyList<PhaseSetupPhase> Phases)
{
    public IReadOnlySet<long> AssignedTeamIds => Phases.SelectMany(x => x.Groups).SelectMany(x => x.TeamIds).ToHashSet();
    public bool AreAllTeamsAssigned => Teams.Count > 0 && Teams.All(x => AssignedTeamIds.Contains(x.Id));
    public bool CanResetSchedule => !HasMatchResults;
}
