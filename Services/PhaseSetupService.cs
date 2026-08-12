using System.ComponentModel.DataAnnotations;
using Competition.Configuration;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Competition.Services;

public sealed class PhaseSetupService : IPhaseSetupService
{
    private readonly CompetitionDbContext dbContext;
    private readonly AnonymousResultEditingSettings anonymousEditing;
    private readonly ILogger<PhaseSetupService> logger;

    public PhaseSetupService(
        CompetitionDbContext dbContext,
        IOptions<AnonymousResultEditingSettings>? anonymousEditingOptions = null,
        ILogger<PhaseSetupService>? logger = null)
    {
        this.dbContext = dbContext;
        anonymousEditing = anonymousEditingOptions?.Value ?? new AnonymousResultEditingSettings();
        this.logger = logger ?? NullLogger<PhaseSetupService>.Instance;
    }

    public async Task<DisciplinePhaseSetup?> GetSetupAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        if (!await EnsurePresetAsync(editionId, competitionDisciplineId, cancellationToken))
        {
            return null;
        }
        var isClosed = await dbContext.CompetitionDisciplines.AsNoTracking()
            .Where(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId)
            .Select(x => x.IsClosed)
            .SingleAsync(cancellationToken);
        if (!isClosed)
        {
            await ReconcileKnockoutAdvancementAsync(editionId, competitionDisciplineId, cancellationToken);
            await ReconcileGroupStandingMatchesAsync(editionId, competitionDisciplineId, cancellationToken);
        }

        var discipline = await dbContext.CompetitionDisciplines.AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.CompetitionEdition).ThenInclude(x => x.Entries).ThenInclude(x => x.Competitor)
            .Include(x => x.Discipline)
            .Include(x => x.Teams).ThenInclude(x => x.Members).ThenInclude(x => x.CompetitionEntry).ThenInclude(x => x.Competitor)
            .Include(x => x.Phases).ThenInclude(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.HomeTeam).ThenInclude(x => x!.Members).ThenInclude(x => x.CompetitionEntry).ThenInclude(x => x.Competitor)
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.AwayTeam).ThenInclude(x => x!.Members).ThenInclude(x => x.CompetitionEntry).ThenInclude(x => x.Competitor)
            .SingleAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);

        var entryLabels = TeamNameFormatter.CreateEntryLabels(discipline.CompetitionEdition.Entries);
        string TeamName(DisciplineTeam team) => TeamNameFormatter.Format(team, entryLabels);

        var allGroups = discipline.Phases.SelectMany(x => x.Groups).ToDictionary(x => x.Id);
        var allMatches = discipline.Phases.SelectMany(x => x.Matches).ToDictionary(x => x.Id);
        var knockoutMatchNames = discipline.Phases
            .Where(x => x.Type == PhaseType.Knockout)
            .SelectMany(phase => phase.Groups.SelectMany(group =>
            {
                var matches = phase.Matches.Where(x => x.PhaseGroupId == group.Id).OrderBy(x => x.Order).ToList();
                return matches.Select((match, index) => new
                {
                    match.Id,
                    Name = matches.Count == 1 ? group.Name : $"{group.Name} {index + 1}"
                });
            }))
            .ToDictionary(x => x.Id, x => x.Name);

        string DisplayMatchName(Match match) => knockoutMatchNames.GetValueOrDefault(match.Id, match.Name);

        string? SourceLabel(Match match, bool home)
        {
            var sourceGroupId = home ? match.HomeSourceGroupId : match.AwaySourceGroupId;
            var sourceRank = home ? match.HomeSourceRank : match.AwaySourceRank;
            if (sourceGroupId is not null && sourceRank is not null && allGroups.TryGetValue(sourceGroupId.Value, out var group))
            {
                return $"{sourceRank}. místo – {group.Name}";
            }

            var sourceMatchId = home ? match.HomeSourceMatchId : match.AwaySourceMatchId;
            return sourceMatchId is not null && allMatches.TryGetValue(sourceMatchId.Value, out var sourceMatch)
                ? $"Vítěz – {DisplayMatchName(sourceMatch)}"
                : null;
        }

        string? AdvancementSource(Match match, bool home)
        {
            var sourceMatchId = home ? match.HomeSourceMatchId : match.AwaySourceMatchId;
            return sourceMatchId is not null && allMatches.TryGetValue(sourceMatchId.Value, out var sourceMatch)
                ? DisplayMatchName(sourceMatch)
                : null;
        }

        PhaseSetupMatch MapMatch(Match match)
        {
            var roundMatch = Regex.Match(match.Name, @"(\d+)\.\s*kolo", RegexOptions.IgnoreCase);
            return new PhaseSetupMatch(
                match.Id, DisplayMatchName(match), match.Order,
                match.HomeTeamId, match.HomeTeam is null ? null : TeamName(match.HomeTeam),
                match.HomeTeam?.Seed,
                match.AwayTeamId, match.AwayTeam is null ? null : TeamName(match.AwayTeam),
                match.AwayTeam?.Seed,
                match.HomeSourceMatchId, match.AwaySourceMatchId,
                SourceLabel(match, true), SourceLabel(match, false),
                AdvancementSource(match, true), AdvancementSource(match, false),
                match.HomeScore, match.AwayScore, match.Version,
                roundMatch.Success ? $"{roundMatch.Groups[1].Value}. kolo" : null,
                match.SetScores.OrderBy(x => x.SetNumber)
                    .Select(x => new PhaseSetupSetScore(x.SetNumber, x.HomeScore, x.AwayScore)).ToList());
        }

        var phases = discipline.Phases.OrderBy(x => x.Order).Select(phase => new PhaseSetupPhase(
            phase.Id, phase.Name, phase.Type, phase.Order, phase.PointsForWin, phase.PointsForDraw, phase.PointsForLoss,
            phase.Groups.OrderBy(x => x.Order).Select(group => new PhaseSetupGroup(
                group.Id, group.Name, group.Order, group.Capacity,
                group.Teams.OrderBy(x => x.Seed).Select(x => x.DisciplineTeamId).ToList(),
                phase.Matches.Where(x => x.PhaseGroupId == group.Id).OrderBy(x => x.Order).Select(MapMatch).ToList())).ToList(),
            phase.Matches.Where(x => x.PhaseGroupId is null).OrderBy(x => x.Order).Select(MapMatch).ToList())).ToList();

        return new DisciplinePhaseSetup(
            discipline.CompetitionEditionId, discipline.CompetitionEdition.Name, discipline.Id, discipline.Discipline.Name,
            discipline.PlayingSystem, discipline.TeamSize, discipline.UsesSetScores, discipline.SetsToWin, discipline.IsScheduleLocked,
            discipline.IsClosed, anonymousEditing.Enabled,
            discipline.Phases.SelectMany(x => x.Matches).Any(x =>
                x.HomeScore != null || x.AwayScore != null || x.SetScores.Count != 0 || x.Status != MatchStatus.Scheduled),
            discipline.Teams.OrderBy(x => x.Seed).Select(x => new PhaseSetupTeam(x.Id, x.Seed, TeamName(x))).ToList(), phases);
    }

    public async Task<long> CreatePhaseAsync(long editionId, long competitionDisciplineId, PhaseInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        await EnsureDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        if (await dbContext.DisciplinePhases.AnyAsync(x => x.CompetitionDisciplineId == competitionDisciplineId && x.Order == input.Order, cancellationToken))
        {
            throw new ValidationException("Fáze s tímto pořadím už existuje.");
        }

        var phase = new DisciplinePhase
        {
            CompetitionDisciplineId = competitionDisciplineId,
            Name = input.Name.Trim(),
            Type = input.Type,
            Order = input.Order,
            PointsForWin = input.PointsForWin,
            PointsForDraw = input.PointsForDraw,
            PointsForLoss = input.PointsForLoss
        };
        dbContext.DisciplinePhases.Add(phase);
        await dbContext.SaveChangesAsync(cancellationToken);
        return phase.Id;
    }

    public async Task<bool> SetPlayingSystemAsync(long editionId, long competitionDisciplineId, PlayingSystemType playingSystem, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(playingSystem))
        {
            throw new ValidationException("Vyberte platný herní systém.");
        }
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Phases).ThenInclude(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (discipline.Phases.SelectMany(x => x.Matches).Any())
        {
            throw new ValidationException("Herní systém nelze změnit po vytvoření zápasů.");
        }
        if (discipline.IsScheduleLocked)
        {
            throw new ValidationException("Herní systém nelze změnit, dokud je rozpis uzamčený.");
        }
        if (discipline.PlayingSystem == playingSystem)
        {
            return true;
        }

        await ClearPhasesAsync(discipline, cancellationToken);
        discipline.PlayingSystem = playingSystem;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<long> CreateGroupAsync(long editionId, long competitionDisciplineId, long phaseId, PhaseGroupInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var phase = await GetPhaseAsync(editionId, competitionDisciplineId, phaseId, cancellationToken);
        if (phase.Type == PhaseType.Knockout && input.Capacity is null)
        {
            throw new ValidationException("U vyřazovací fáze zadejte počet týmů v etapě.");
        }
        if (phase.Type != PhaseType.Group && phase.Type != PhaseType.Knockout)
        {
            throw new ValidationException("Do této fáze nelze přidat skupinu ani etapu.");
        }
        if (await dbContext.PhaseGroups.AnyAsync(x => x.DisciplinePhaseId == phaseId && (x.Order == input.Order || x.Name == input.Name.Trim()), cancellationToken))
        {
            throw new ValidationException("Položka se stejným názvem nebo pořadím už existuje.");
        }
        if (phase.Type == PhaseType.Knockout)
        {
            var capacity = input.Capacity!.Value;
            if (capacity < 2 || capacity % 2 != 0)
            {
                throw new ValidationException("Etapa musí mít sudý počet týmů alespoň 2.");
            }

            var previousWinnerCount = await GetKnockoutPreviousWinnerCountAsync(phaseId, input.Order, cancellationToken);
            if (capacity < previousWinnerCount)
            {
                throw new ValidationException(
                    $"Etapa {input.Name.Trim()} musí mít alespoň {previousWinnerCount} týmů, protože tolik týmů postupuje z předchozí etapy.");
            }
        }

        var group = new PhaseGroup
        {
            DisciplinePhaseId = phaseId,
            Name = input.Name.Trim(),
            Order = input.Order,
            Capacity = phase.Type == PhaseType.Knockout ? input.Capacity : null
        };
        dbContext.PhaseGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return group.Id;
    }

    public async Task<bool> AssignGroupTeamsAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, IReadOnlyCollection<long> teamIds, CancellationToken cancellationToken = default)
    {
        var group = await GetGroupAsync(editionId, competitionDisciplineId, phaseId, groupId, cancellationToken);
        var distinctIds = teamIds.Where(x => x > 0).Distinct().ToList();
        var directTeamCapacity = group.Capacity;
        if (group.DisciplinePhase.Type == PhaseType.Knockout && group.Capacity is not null)
        {
            var previousWinnerCount = await GetKnockoutPreviousWinnerCountAsync(phaseId, group.Order, cancellationToken);
            directTeamCapacity = Math.Max(0, group.Capacity.Value - previousWinnerCount);
        }
        if (directTeamCapacity is not null && distinctIds.Count > directTeamCapacity)
        {
            throw new ValidationException($"Do etapy lze přímo přiřadit nejvýše {directTeamCapacity} týmů.");
        }

        var validCount = await dbContext.DisciplineTeams.CountAsync(
            x => x.CompetitionDisciplineId == competitionDisciplineId && distinctIds.Contains(x.Id), cancellationToken);
        if (validCount != distinctIds.Count)
        {
            throw new ValidationException("Některý tým nepatří do této disciplíny.");
        }

        if (await dbContext.PhaseGroupTeams
            .Where(x => x.PhaseGroupId != groupId && distinctIds.Contains(x.DisciplineTeamId))
            .AnyAsync(x => x.PhaseGroup.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId, cancellationToken))
        {
            throw new ValidationException("Některý tým už je přiřazen do jiné skupiny nebo etapy.");
        }
        if (await dbContext.Matches.AnyAsync(x => x.DisciplinePhaseId == phaseId, cancellationToken))
        {
            throw new ValidationException("Přiřazení nelze změnit po vytvoření zápasů.");
        }

        var assignments = await dbContext.PhaseGroupTeams
            .Where(x => x.DisciplinePhaseId == phaseId && x.PhaseGroupId == groupId)
            .ToListAsync(cancellationToken);
        dbContext.PhaseGroupTeams.RemoveRange(assignments);
        for (var index = 0; index < distinctIds.Count; index++)
        {
            dbContext.PhaseGroupTeams.Add(new PhaseGroupTeam
            {
                DisciplinePhaseId = phaseId,
                PhaseGroupId = groupId,
                DisciplineTeamId = distinctIds[index],
                Seed = index + 1
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteGroupAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, CancellationToken cancellationToken = default)
    {
        var group = await dbContext.PhaseGroups
            .Include(x => x.Teams)
            .Include(x => x.Matches)
            .Include(x => x.DisciplinePhase).ThenInclude(x => x.CompetitionDiscipline)
            .SingleOrDefaultAsync(x => x.Id == groupId && x.DisciplinePhaseId == phaseId &&
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId,
                cancellationToken);
        if (group is null)
        {
            return false;
        }
        EnsureOpen(group.DisciplinePhase.CompetitionDiscipline);
        if (group.Teams.Count != 0)
        {
            throw new ValidationException("Skupinu nebo etapu nelze smazat, dokud jsou do ní přiřazené týmy.");
        }
        if (group.Matches.Count != 0)
        {
            throw new ValidationException("Skupinu nebo etapu nelze smazat po vytvoření zápasů.");
        }

        dbContext.PhaseGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeletePhaseAsync(long editionId, long competitionDisciplineId, long phaseId, CancellationToken cancellationToken = default)
    {
        var phase = await dbContext.DisciplinePhases
            .Include(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Matches)
            .Include(x => x.CompetitionDiscipline)
            .SingleOrDefaultAsync(x => x.Id == phaseId && x.CompetitionDisciplineId == competitionDisciplineId &&
                x.CompetitionDiscipline.CompetitionEditionId == editionId,
                cancellationToken);
        if (phase is null)
        {
            return false;
        }
        EnsureOpen(phase.CompetitionDiscipline);
        if (phase.CompetitionDiscipline.PlayingSystem is not PlayingSystemType.Custom and not PlayingSystemType.Knockout)
        {
            throw new ValidationException("Fáze tohoto herního systému je pevně daná a nelze ji smazat.");
        }
        if (phase.Groups.SelectMany(x => x.Teams).Any())
        {
            throw new ValidationException("Fázi nelze smazat, dokud jsou do ní přiřazené týmy.");
        }
        if (phase.Matches.Count != 0 || phase.Groups.SelectMany(x => x.Matches).Any())
        {
            throw new ValidationException("Fázi nelze smazat po vytvoření zápasů.");
        }

        dbContext.PhaseGroups.RemoveRange(phase.Groups);
        dbContext.DisciplinePhases.Remove(phase);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RandomlyAssignGroupTeamsAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, int teamCount, CancellationToken cancellationToken = default)
    {
        if (teamCount < 1)
        {
            throw new ValidationException("Počet týmů musí být kladný.");
        }
        var group = await GetGroupAsync(editionId, competitionDisciplineId, phaseId, groupId, cancellationToken);
        if (await dbContext.Matches.AnyAsync(x => x.DisciplinePhaseId == phaseId, cancellationToken))
        {
            throw new ValidationException("Přiřazení nelze změnit po vytvoření zápasů.");
        }

        var currentIds = await dbContext.PhaseGroupTeams
            .Where(x => x.DisciplinePhaseId == phaseId && x.PhaseGroupId == groupId)
            .OrderBy(x => x.Seed).Select(x => x.DisciplineTeamId).ToListAsync(cancellationToken);
        var directTeamCapacity = group.Capacity;
        if (group.DisciplinePhase.Type == PhaseType.Knockout && group.Capacity is not null)
        {
            var previousWinnerCount = await GetKnockoutPreviousWinnerCountAsync(phaseId, group.Order, cancellationToken);
            directTeamCapacity = Math.Max(0, group.Capacity.Value - previousWinnerCount);
        }
        if (directTeamCapacity is not null && currentIds.Count + teamCount > directTeamCapacity)
        {
            throw new ValidationException($"Do etapy lze přímo přiřadit nejvýše {directTeamCapacity} týmů.");
        }

        var assignedIds = await dbContext.PhaseGroupTeams
            .Where(x => x.PhaseGroup.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId)
            .Select(x => x.DisciplineTeamId).ToListAsync(cancellationToken);
        var availableIds = await dbContext.DisciplineTeams
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId && !assignedIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (availableIds.Count < teamCount)
        {
            throw new ValidationException("Pro náhodné přiřazení není k dispozici dost týmů.");
        }

        var selected = availableIds.OrderBy(_ => Random.Shared.Next()).Take(teamCount).ToList();
        for (var index = 0; index < selected.Count; index++)
        {
            dbContext.PhaseGroupTeams.Add(new PhaseGroupTeam
            {
                DisciplinePhaseId = phaseId,
                PhaseGroupId = groupId,
                DisciplineTeamId = selected[index],
                Seed = currentIds.Count + index + 1
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return selected.Count;
    }

    private async Task<int> GetKnockoutPreviousWinnerCountAsync(long phaseId, int stageOrder, CancellationToken cancellationToken)
    {
        var previousCapacity = await dbContext.PhaseGroups
            .Where(x => x.DisciplinePhaseId == phaseId && x.Order < stageOrder)
            .OrderByDescending(x => x.Order)
            .Select(x => x.Capacity)
            .FirstOrDefaultAsync(cancellationToken);

        return previousCapacity.GetValueOrDefault() / 2;
    }

    public async Task<int> GenerateRoundRobinAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, CancellationToken cancellationToken = default)
    {
        var group = await GetGroupAsync(editionId, competitionDisciplineId, phaseId, groupId, cancellationToken);
        var teamIds = await dbContext.PhaseGroupTeams
            .Where(x => x.DisciplinePhaseId == phaseId && x.PhaseGroupId == groupId)
            .OrderBy(x => x.Seed).Select(x => x.DisciplineTeamId).ToListAsync(cancellationToken);
        EnsureRoundRobinCanBeGenerated(teamIds.Count);
        if (await dbContext.Matches.AnyAsync(x => x.DisciplinePhaseId == phaseId && x.PhaseGroupId == groupId, cancellationToken))
        {
            throw new ValidationException("Skupina už má vytvořené zápasy.");
        }

        var count = AddBergerMatches(phaseId, group.Id, group.Name, teamIds);
        await dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    public Task<int> GeneratePresetMatchesAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default) =>
        GeneratePresetMatchesInternalAsync(editionId, competitionDisciplineId, null, cancellationToken);

    public async Task<int> GenerateKnockoutMatchesAsync(long editionId, long competitionDisciplineId, bool randomizeTeams, CancellationToken cancellationToken = default)
    {
        await EnsureDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        var isKnockout = await dbContext.CompetitionDisciplines.AnyAsync(x => x.Id == competitionDisciplineId &&
            x.CompetitionEditionId == editionId && x.PlayingSystem == PlayingSystemType.Knockout, cancellationToken);
        if (!isKnockout)
        {
            throw new ValidationException("Tímto způsobem lze vytvořit pouze vyřazovací pavouk.");
        }

        return await GeneratePresetMatchesInternalAsync(editionId, competitionDisciplineId, randomizeTeams, cancellationToken);
    }

    private async Task<int> GeneratePresetMatchesInternalAsync(long editionId, long competitionDisciplineId, bool? randomizeKnockoutTeams, CancellationToken cancellationToken)
    {
        await EnsurePresetAsync(editionId, competitionDisciplineId, cancellationToken);
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Matches)
            .SingleAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        EnsureOpen(discipline);

        if (discipline.Phases.SelectMany(x => x.Matches).Any())
        {
            throw new ValidationException("Zápasy už byly vytvořeny.");
        }

        if (discipline.PlayingSystem == PlayingSystemType.RoundRobin)
        {
            var phase = discipline.Phases.Single(x => x.Type == PhaseType.Group);
            var group = phase.Groups.Single();
            var teamIds = group.Teams.OrderBy(x => x.Seed).Select(x => x.DisciplineTeamId).ToList();
            EnsureRoundRobinCanBeGenerated(teamIds.Count);
            var count = AddBergerMatches(phase.Id, group.Id, group.Name, teamIds);
            await dbContext.SaveChangesAsync(cancellationToken);
            return count;
        }

        if (discipline.PlayingSystem == PlayingSystemType.GroupsThenClassificationMatches)
        {
            var groupPhases = discipline.Phases.Where(x => x.Type == PhaseType.Group).OrderBy(x => x.Order).ToList();
            var groups = groupPhases.Select(x => x.Groups.Single()).ToList();
            var assignedIds = groups.SelectMany(x => x.Teams).Select(x => x.DisciplineTeamId).ToList();
            if (assignedIds.Count != discipline.Teams.Count || assignedIds.Distinct().Count() != discipline.Teams.Count)
            {
                throw new ValidationException("Nejprve rozdělte všechny týmy právě do jedné skupiny.");
            }
            if (groups.Any(x => x.Teams.Count < 2))
            {
                throw new ValidationException("V každé skupině musí být alespoň dva týmy.");
            }

            var total = 0;
            foreach (var group in groups)
            {
                total += AddBergerMatches(group.DisciplinePhaseId, group.Id, group.Name,
                    group.Teams.OrderBy(x => x.Seed).Select(x => x.DisciplineTeamId).ToList());
            }

            var finalPhase = discipline.Phases.Single(x => x.Type == PhaseType.FinalStanding);
            dbContext.Matches.AddRange(
                new Match
                {
                    DisciplinePhaseId = finalPhase.Id,
                    Name = "Finále",
                    Order = 1,
                    Status = MatchStatus.Scheduled,
                    HomeSourceGroupId = groups[0].Id,
                    HomeSourceRank = 1,
                    AwaySourceGroupId = groups[1].Id,
                    AwaySourceRank = 1
                },
                new Match
                {
                    DisciplinePhaseId = finalPhase.Id,
                    Name = "O 3. místo",
                    Order = 2,
                    Status = MatchStatus.Scheduled,
                    HomeSourceGroupId = groups[0].Id,
                    HomeSourceRank = 2,
                    AwaySourceGroupId = groups[1].Id,
                    AwaySourceRank = 2
                });
            total += 2;
            await dbContext.SaveChangesAsync(cancellationToken);
            return total;
        }

        if (discipline.PlayingSystem == PlayingSystemType.Knockout)
        {
            var phase = discipline.Phases.Single(x => x.Type == PhaseType.Knockout);
            var stages = phase.Groups.OrderBy(x => x.Order).ToList();
            if (stages.Count == 0)
            {
                throw new ValidationException("Nejprve vytvořte alespoň jednu vyřazovací etapu.");
            }

            var previousWinnerCount = 0;
            foreach (var stage in stages)
            {
                if (stage.Capacity is null || stage.Capacity < 2 || stage.Capacity % 2 != 0)
                {
                    throw new ValidationException($"Etapa {stage.Name} musí mít sudý počet týmů alespoň 2.");
                }
                if (stage.Teams.Count + previousWinnerCount != stage.Capacity)
                {
                    throw new ValidationException(
                        $"Etapa {stage.Name} potřebuje {stage.Capacity} účastníků; přímých týmů a vítězů předchozí etapy je {stage.Teams.Count + previousWinnerCount}.");
                }
                previousWinnerCount = (stage.Teams.Count + previousWinnerCount) / 2;
            }

            var total = 0;
            var previousMatches = new List<Match>();
            foreach (var stage in stages)
            {
                var orderedDirectTeams = randomizeKnockoutTeams == true
                    ? stage.Teams.OrderBy(_ => Random.Shared.Next()).ToList()
                    : stage.Teams.OrderBy(x => x.Seed).ToList();
                var sources = randomizeKnockoutTeams == false
                    ? Enumerable.Range(0, orderedDirectTeams.Count + previousMatches.Count).Select(_ => new BracketSource(null, null)).ToList()
                    : orderedDirectTeams.Select(x => new BracketSource(x.DisciplineTeamId, null))
                        .Concat(previousMatches.Select(x => new BracketSource(null, x.Id)))
                        .ToList();
                var stageMatches = new List<Match>();
                for (var index = 0; index < sources.Count; index += 2)
                {
                    stageMatches.Add(new Match
                    {
                        DisciplinePhaseId = phase.Id,
                        PhaseGroupId = stage.Id,
                        Name = sources.Count == 2 ? stage.Name : $"{stage.Name} {index / 2 + 1}",
                        Order = total + index / 2 + 1,
                        Status = MatchStatus.Scheduled,
                        HomeTeamId = sources[index].TeamId,
                        HomeSourceMatchId = sources[index].MatchId,
                        AwayTeamId = sources[index + 1].TeamId,
                        AwaySourceMatchId = sources[index + 1].MatchId
                    });
                }
                dbContext.Matches.AddRange(stageMatches);
                await dbContext.SaveChangesAsync(cancellationToken);
                previousMatches = stageMatches;
                total += stageMatches.Count;
            }
            return total;
        }

        throw new ValidationException("Pro tento herní systém zatím automatické generování není připraveno.");
    }

    public async Task<long> CreateMatchSlotAsync(long editionId, long competitionDisciplineId, long phaseId, MatchSlotInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        await GetPhaseAsync(editionId, competitionDisciplineId, phaseId, cancellationToken);
        if (input.HomeTeamId == input.AwayTeamId && input.HomeTeamId is not null)
        {
            throw new ValidationException("Tým nemůže hrát sám proti sobě.");
        }
        var suppliedIds = new[] { input.HomeTeamId, input.AwayTeamId }.OfType<long>().Distinct().ToList();
        var validCount = await dbContext.DisciplineTeams.CountAsync(
            x => x.CompetitionDisciplineId == competitionDisciplineId && suppliedIds.Contains(x.Id), cancellationToken);
        if (validCount != suppliedIds.Count)
        {
            throw new ValidationException("Některý tým nepatří do této disciplíny.");
        }
        var order = (await dbContext.Matches.Where(x => x.DisciplinePhaseId == phaseId)
            .Select(x => (int?)x.Order).MaxAsync(cancellationToken) ?? 0) + 1;
        var match = new Match
        {
            DisciplinePhaseId = phaseId,
            HomeTeamId = input.HomeTeamId,
            AwayTeamId = input.AwayTeamId,
            Name = input.Name.Trim(),
            Order = order,
            Status = MatchStatus.Scheduled
        };
        dbContext.Matches.Add(match);
        await dbContext.SaveChangesAsync(cancellationToken);
        return match.Id;
    }

    public async Task<bool> UpdateKnockoutMatchTeamsAsync(long editionId, long competitionDisciplineId, MatchTeamsInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var match = await dbContext.Matches
            .Include(x => x.PhaseGroup).ThenInclude(x => x!.Teams)
            .Include(x => x.DisciplinePhase).ThenInclude(x => x.CompetitionDiscipline)
            .Include(x => x.DisciplinePhase).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .SingleOrDefaultAsync(x => x.Id == input.MatchId &&
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId, cancellationToken)
            ?? throw new ValidationException("Zápas neexistuje.");

        var discipline = match.DisciplinePhase.CompetitionDiscipline;
        EnsureOpen(discipline);
        if (discipline.PlayingSystem != PlayingSystemType.Knockout || match.PhaseGroup is null)
        {
            throw new ValidationException("Ručně lze měnit pouze obsazení vyřazovacího zápasu.");
        }
        if (discipline.IsScheduleLocked)
        {
            throw new ValidationException("Obsazení zápasu nelze měnit po uzamčení rozpisu.");
        }
        if (match.DisciplinePhase.Matches.Any(HasResult))
        {
            throw new ValidationException("Obsazení zápasů nelze měnit po zapsání výsledků.");
        }

        var (selectedHomeTeamId, selectedHomeSourceMatchId) = ResolveMatchSlotSelection(input.HomeSelection, input.HomeTeamId, match.HomeSourceMatchId);
        var (selectedAwayTeamId, selectedAwaySourceMatchId) = ResolveMatchSlotSelection(input.AwaySelection, input.AwayTeamId, match.AwaySourceMatchId);
        var allowedIds = match.PhaseGroup.Teams.Select(x => x.DisciplineTeamId).ToHashSet();
        var previousStageSourceIds = await dbContext.Matches
            .Where(x => x.DisciplinePhaseId == match.DisciplinePhaseId &&
                x.PhaseGroup != null &&
                x.PhaseGroup.Order == match.PhaseGroup.Order - 1)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var allowedSourceIds = previousStageSourceIds.ToHashSet();
        var homeTeamId = selectedHomeTeamId;
        var awayTeamId = selectedAwayTeamId;
        var suppliedIds = new[] { homeTeamId, awayTeamId }.OfType<long>().ToList();
        if (suppliedIds.Count != suppliedIds.Distinct().Count())
        {
            throw new ValidationException("Tým nemůže hrát sám proti sobě.");
        }
        if (suppliedIds.Any(x => !allowedIds.Contains(x)))
        {
            throw new ValidationException("Vybraný tým není přiřazen do této etapy.");
        }
        var suppliedSourceMatchIds = new[] { selectedHomeSourceMatchId, selectedAwaySourceMatchId }.OfType<long>().ToList();
        if (suppliedSourceMatchIds.Count != suppliedSourceMatchIds.Distinct().Count())
        {
            throw new ValidationException("Stejný postupující tým nelze nasadit do obou pozic zápasu.");
        }
        if (suppliedSourceMatchIds.Any(x => !allowedSourceIds.Contains(x)))
        {
            throw new ValidationException("Vybraný postupující tým nepatří do předchozí etapy.");
        }

        var usedByOtherMatches = match.DisciplinePhase.Matches
            .Where(x => x.PhaseGroupId == match.PhaseGroupId && x.Id != match.Id)
            .SelectMany(x => new[]
            {
                x.HomeSourceMatchId is null ? x.HomeTeamId : null,
                x.AwaySourceMatchId is null ? x.AwayTeamId : null
            })
            .OfType<long>()
            .ToHashSet();
        if (suppliedIds.Any(usedByOtherMatches.Contains))
        {
            throw new ValidationException("Vybraný tým už je nasazen do jiného zápasu této etapy.");
        }
        var usedSourceMatchesByOtherMatches = match.DisciplinePhase.Matches
            .Where(x => x.PhaseGroupId == match.PhaseGroupId && x.Id != match.Id)
            .SelectMany(x => new[] { x.HomeSourceMatchId, x.AwaySourceMatchId })
            .OfType<long>()
            .ToHashSet();
        if (suppliedSourceMatchIds.Any(usedSourceMatchesByOtherMatches.Contains))
        {
            throw new ValidationException("Vybraný postupující tým už je nasazen do jiného zápasu této etapy.");
        }

        match.HomeTeamId = selectedHomeTeamId;
        match.HomeSourceMatchId = selectedHomeSourceMatchId;
        match.AwayTeamId = selectedAwayTeamId;
        match.AwaySourceMatchId = selectedAwaySourceMatchId;
        match.Version++;
        match.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static (long? TeamId, long? SourceMatchId) ResolveMatchSlotSelection(string? selection, long? legacyTeamId, long? legacySourceMatchId)
    {
        if (selection is null)
        {
            return legacyTeamId is not null ? (legacyTeamId, null) : (null, legacySourceMatchId);
        }
        if (string.IsNullOrWhiteSpace(selection) || selection == "clear")
        {
            return (null, null);
        }

        var parts = selection.Split(':', 2);
        if (parts.Length != 2 || !long.TryParse(parts[1], out var id) || id <= 0)
        {
            throw new ValidationException("Vyberte platnou pozici v zápasu.");
        }

        return parts[0] switch
        {
            "team" => (id, null),
            "match" => (null, id),
            _ => throw new ValidationException("Vyberte platnou pozici v zápasu.")
        };
    }

    public async Task<bool> ResetScheduleAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Phases).ThenInclude(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (discipline.IsScheduleLocked)
        {
            throw new ValidationException("Uzamčený rozpis nelze smazat. Nejprve jej odemkněte.");
        }

        var matches = discipline.Phases.SelectMany(x => x.Matches).ToList();
        var hasResults = matches.Any(x => x.HomeScore != null || x.AwayScore != null ||
            x.Status != MatchStatus.Scheduled || x.SetScores.Count != 0);
        if (hasResults)
        {
            throw new ValidationException("Rozpis s uloženými výsledky nelze smazat.");
        }

        dbContext.MatchSetScores.RemoveRange(matches.SelectMany(x => x.SetScores));
        dbContext.Matches.RemoveRange(matches);
        dbContext.PhaseGroupTeams.RemoveRange(discipline.Phases.SelectMany(x => x.Groups).SelectMany(x => x.Teams));
        discipline.IsScheduleLocked = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetScheduleLockAsync(long editionId, long competitionDisciplineId, bool isLocked, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        if (isLocked && !await dbContext.Matches.AnyAsync(
            x => x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId, cancellationToken))
        {
            throw new ValidationException("Prázdný rozpis nelze uzamknout.");
        }
        if (!isLocked && await dbContext.Matches.AnyAsync(x =>
            x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
            (x.HomeScore != null || x.AwayScore != null || x.SetScores.Any() || x.Status != MatchStatus.Scheduled), cancellationToken))
        {
            throw new ValidationException("Rozpis s výsledky nelze odemknout. Nejprve smažte výsledky.");
        }
        if (isLocked && discipline.PlayingSystem == PlayingSystemType.Knockout)
        {
            var hasEmptyDirectSlot = await dbContext.Matches.AnyAsync(x =>
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                ((x.HomeSourceMatchId == null && x.HomeTeamId == null) ||
                 (x.AwaySourceMatchId == null && x.AwayTeamId == null)), cancellationToken);
            if (hasEmptyDirectSlot)
            {
                throw new ValidationException("Před uzamčením rozpisu obsadťe všechny ručně nastavované pozice.");
            }
        }

        discipline.IsScheduleLocked = isLocked;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteResultsAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsRelational())
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var result = await DeleteResultsCoreAsync(editionId, competitionDisciplineId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        return await DeleteResultsCoreAsync(editionId, competitionDisciplineId, cancellationToken);
    }

    private async Task<bool> DeleteResultsCoreAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Phases).ThenInclude(x => x.Matches).ThenInclude(x => x.SetScores)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }
        EnsureOpen(discipline);
        var matchesInReverseProgression = discipline.Phases
            .OrderByDescending(x => x.Order)
            .SelectMany(x => x.Matches.OrderByDescending(m => m.Order))
            .ToList();
        foreach (var match in matchesInReverseProgression)
        {
            dbContext.MatchSetScores.RemoveRange(match.SetScores);
            match.HomeScore = null;
            match.AwayScore = null;
            match.Status = MatchStatus.Scheduled;
            if (match.HomeSourceMatchId is not null || match.HomeSourceGroupId is not null)
            {
                match.HomeTeamId = null;
            }
            if (match.AwaySourceMatchId is not null || match.AwaySourceGroupId is not null)
            {
                match.AwayTeamId = null;
            }
            match.Version++;
            match.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> GenerateRandomResultsAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            return await GenerateRandomResultsCoreAsync(editionId, competitionDisciplineId, cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await GenerateRandomResultsCoreAsync(editionId, competitionDisciplineId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<int> GenerateRandomResultsCoreAsync(
        long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        if (!await DeleteResultsCoreAsync(editionId, competitionDisciplineId, cancellationToken))
        {
            throw new ValidationException("Disciplína neexistuje.");
        }

        var generatedCount = 0;
        while (true)
        {
            var match = await dbContext.Matches.AsNoTracking()
                .Where(x => x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                    x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId &&
                    x.HomeTeamId != null && x.AwayTeamId != null &&
                    (x.HomeScore == null || x.AwayScore == null))
                .OrderBy(x => x.DisciplinePhase.Order)
                .ThenBy(x => x.Order)
                .ThenBy(x => x.Id)
                .Select(x => new RandomResultMatch(
                    x.Id,
                    x.Version,
                    x.DisciplinePhase.CompetitionDiscipline.UsesSetScores,
                    x.DisciplinePhase.CompetitionDiscipline.SetsToWin))
                .FirstOrDefaultAsync(cancellationToken);
            if (match is null)
            {
                break;
            }

            var result = CreateRandomResult(match.UsesSetScores, match.SetsToWin);
            var version = match.Version;
            if (match.UsesSetScores)
            {
                await UpdateMatchSetScoresAsync(editionId, competitionDisciplineId, new MatchSetScoresInput
                {
                    MatchId = match.Id,
                    Version = version,
                    Sets = result.Sets
                }, true, cancellationToken);
                version = await dbContext.Matches.AsNoTracking()
                    .Where(x => x.Id == match.Id)
                    .Select(x => x.Version)
                    .SingleAsync(cancellationToken);
            }

            await UpdateMatchResultAsync(editionId, competitionDisciplineId, new MatchResultInput
            {
                MatchId = match.Id,
                HomeScore = result.HomeScore,
                AwayScore = result.AwayScore,
                Version = version
            }, true, cancellationToken);
            generatedCount++;
        }

        var unresolvedMatches = await dbContext.Matches.AsNoTracking().AnyAsync(x =>
            x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
            x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId &&
            (x.HomeScore == null || x.AwayScore == null), cancellationToken);
        if (unresolvedMatches)
        {
            throw new ValidationException("Některé zápasy nemají přiřazené oba týmy, proto nelze vygenerovat všechny výsledky.");
        }

        return generatedCount;
    }

    private static RandomMatchResult CreateRandomResult(bool usesSetScores, int? configuredSetsToWin)
    {
        var homeWins = Random.Shared.Next(2) == 0;
        if (!usesSetScores)
        {
            var winningScore = Random.Shared.Next(1, 6);
            var losingScore = Random.Shared.Next(0, winningScore);
            return new RandomMatchResult(
                homeWins ? winningScore : losingScore,
                homeWins ? losingScore : winningScore,
                []);
        }

        var setsToWin = configuredSetsToWin
            ?? throw new ValidationException("U disciplíny není nastaven počet vítězných setů.");
        var losingSetWins = Random.Shared.Next(setsToWin);
        var setWinners = Enumerable.Repeat(homeWins, setsToWin - 1)
            .Concat(Enumerable.Repeat(!homeWins, losingSetWins))
            .OrderBy(_ => Random.Shared.Next())
            .Append(homeWins)
            .ToList();
        var sets = setWinners.Select((isHomeWinner, index) =>
        {
            var losingScore = Random.Shared.Next(0, 10);
            return new MatchSetScoreInput
            {
                SetNumber = index + 1,
                HomeScore = isHomeWinner ? 10 : losingScore,
                AwayScore = isHomeWinner ? losingScore : 10
            };
        }).ToList();
        return new RandomMatchResult(
            homeWins ? setsToWin : losingSetWins,
            homeWins ? losingSetWins : setsToWin,
            sets);
    }

    public async Task<bool> UpdateMatchResultAsync(long editionId, long competitionDisciplineId, MatchResultInput input, bool isAdmin, CancellationToken cancellationToken = default)
    {
        Validate(input);
        if ((input.HomeScore is null) != (input.AwayScore is null))
        {
            throw new ValidationException("Vyplňte obě hodnoty hlavního skóre, nebo obě ponechte prázdné.");
        }

        var match = await LoadMatchForEditingAsync(editionId, competitionDisciplineId, input.MatchId, cancellationToken);
        EnsureMatchCanBeEdited(match, input.Version, isAdmin);
        if ((match.HomeTeamId is null || match.AwayTeamId is null) && input.HomeScore is not null)
        {
            throw new ValidationException("Výsledek nelze zapsat, dokud nejsou známy oba týmy.");
        }
        if (match.HomeTeamId is not null && match.HomeTeamId == match.AwayTeamId)
        {
            throw new ValidationException("Tým nemůže hrát sám proti sobě.");
        }
        ValidateMainScore(match, input.HomeScore, input.AwayScore);

        var previousHomeScore = match.HomeScore;
        var previousAwayScore = match.AwayScore;
        var previousStatus = match.Status;
        dbContext.Entry(match).Property(x => x.Version).OriginalValue = input.Version;
        match.HomeScore = input.HomeScore;
        match.AwayScore = input.AwayScore;
        match.Status = input.HomeScore is not null
            ? MatchStatus.Completed
            : match.SetScores.Count == 0 ? MatchStatus.Scheduled : MatchStatus.InProgress;
        ValidateSetResultConsistency(match, match.SetScores
            .Select(x => new SetResult(x.SetNumber, x.HomeScore, x.AwayScore)).ToList());
        LockScheduleAfterResult(match);
        await UpdateKnockoutAdvancementAsync(match, cancellationToken);
        await SaveMatchEditAsync(cancellationToken);
        await ReconcileGroupStandingMatchesAsync(editionId, competitionDisciplineId, cancellationToken);
        logger.LogInformation(
            "Match result changed. EditionId={EditionId} DisciplineId={DisciplineId} MatchId={MatchId} Actor={Actor} OldScore={OldHomeScore}:{OldAwayScore} NewScore={NewHomeScore}:{NewAwayScore} OldStatus={OldStatus} NewStatus={NewStatus} SubmittedVersion={SubmittedVersion} SavedVersion={SavedVersion}",
            editionId, competitionDisciplineId, match.Id, isAdmin ? "Admin" : "Anonymous",
            previousHomeScore, previousAwayScore, input.HomeScore, input.AwayScore,
            previousStatus, match.Status, input.Version, match.Version);
        return true;
    }

    public async Task<bool> UpdateMatchSetScoresAsync(long editionId, long competitionDisciplineId, MatchSetScoresInput input, bool isAdmin, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var match = await LoadMatchForEditingAsync(editionId, competitionDisciplineId, input.MatchId, cancellationToken);
        EnsureMatchCanBeEdited(match, input.Version, isAdmin);
        if (!match.DisciplinePhase.CompetitionDiscipline.UsesSetScores)
        {
            throw new ValidationException("Tato disciplína nemá povolené dílčí skóre.");
        }
        if (match.HomeTeamId is null || match.AwayTeamId is null)
        {
            throw new ValidationException("Dílčí skóre nelze zapsat, dokud nejsou známy oba týmy.");
        }

        if (match.HomeTeamId == match.AwayTeamId)
        {
            throw new ValidationException("Tým nemůže hrát sám proti sobě.");
        }

        foreach (var set in input.Sets)
        {
            Validate(set);
        }
        var suppliedSets = input.Sets.Where(x => x.HomeScore is not null || x.AwayScore is not null).ToList();
        if (suppliedSets.Any(x => x.HomeScore is null || x.AwayScore is null))
        {
            throw new ValidationException("U každého setu vyplňte obě hodnoty.");
        }
        var setsToWin = match.DisciplinePhase.CompetitionDiscipline.SetsToWin
            ?? throw new ValidationException("U disciplíny není nastaven počet vítězných setů.");
        var maximumSets = setsToWin * 2 - 1;
        if (suppliedSets.Select(x => x.SetNumber).Distinct().Count() != suppliedSets.Count ||
            suppliedSets.Any(x => x.SetNumber < 1 || x.SetNumber > maximumSets))
        {
            throw new ValidationException($"Čísla setů musí být jedinečná a v rozsahu 1 až {maximumSets}.");
        }
        var orderedSets = suppliedSets.OrderBy(x => x.SetNumber).ToList();
        if (!orderedSets.Select(x => x.SetNumber).SequenceEqual(Enumerable.Range(1, orderedSets.Count)))
        {
            throw new ValidationException("Výsledky setů vyplňujte postupně bez vynechaných setů.");
        }
        if (orderedSets.Any(x => x.HomeScore == x.AwayScore))
        {
            throw new ValidationException("Set musí mít vítěze.");
        }

        ValidateSetResultConsistency(match, orderedSets
            .Select(x => new SetResult(x.SetNumber, x.HomeScore!.Value, x.AwayScore!.Value)).ToList());

        var previousSets = match.SetScores.OrderBy(x => x.SetNumber)
            .Select(x => $"{x.SetNumber}:{x.HomeScore}-{x.AwayScore}").ToArray();
        dbContext.Entry(match).Property(x => x.Version).OriginalValue = input.Version;
        dbContext.MatchSetScores.RemoveRange(match.SetScores);
        foreach (var set in suppliedSets.OrderBy(x => x.SetNumber))
        {
            dbContext.MatchSetScores.Add(new MatchSetScore
            {
                MatchId = match.Id,
                SetNumber = set.SetNumber,
                HomeScore = set.HomeScore!.Value,
                AwayScore = set.AwayScore!.Value
            });
        }
        match.Status = match.HomeScore is not null
            ? MatchStatus.Completed
            : suppliedSets.Count == 0 ? MatchStatus.Scheduled : MatchStatus.InProgress;
        match.UpdatedAtUtc = DateTime.UtcNow;
        LockScheduleAfterResult(match);
        await SaveMatchEditAsync(cancellationToken);
        await ReconcileGroupStandingMatchesAsync(editionId, competitionDisciplineId, cancellationToken);
        logger.LogInformation(
            "Match set scores changed. EditionId={EditionId} DisciplineId={DisciplineId} MatchId={MatchId} Actor={Actor} OldSets={OldSets} NewSets={NewSets} SubmittedVersion={SubmittedVersion} SavedVersion={SavedVersion}",
            editionId, competitionDisciplineId, match.Id, isAdmin ? "Admin" : "Anonymous",
            string.Join(",", previousSets),
            string.Join(",", suppliedSets.OrderBy(x => x.SetNumber).Select(x => $"{x.SetNumber}:{x.HomeScore}-{x.AwayScore}")),
            input.Version, match.Version);
        return true;
    }

    private async Task<Match> LoadMatchForEditingAsync(long editionId, long competitionDisciplineId, long matchId, CancellationToken cancellationToken) =>
        await dbContext.Matches
            .Include(x => x.SetScores)
            .Include(x => x.DisciplinePhase).ThenInclude(x => x.CompetitionDiscipline)
            .SingleOrDefaultAsync(x => x.Id == matchId &&
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId, cancellationToken)
        ?? throw new ValidationException("Zápas neexistuje.");

    private void EnsureMatchCanBeEdited(Match match, int version, bool isAdmin)
    {
        if (match.Version != version)
        {
            throw new ValidationException("Výsledek mezitím změnil někdo jiný. Obnovte stránku a zkuste to znovu.");
        }
        var discipline = match.DisciplinePhase.CompetitionDiscipline;
        EnsureOpen(discipline);
        if (!isAdmin && !anonymousEditing.Enabled)
        {
            throw new ValidationException("Veřejná editace výsledků je momentálně uzavřená.");
        }
        if (discipline.PlayingSystem == PlayingSystemType.Knockout && !discipline.IsScheduleLocked)
        {
            throw new ValidationException("Výsledek vyřazovacího zápasu lze zapsat až po uzamčení rozpisu.");
        }
    }

    private static bool HasResult(Match match) => match.HomeScore is not null || match.AwayScore is not null ||
        match.SetScores.Count != 0 || match.Status != MatchStatus.Scheduled;

    private static void LockScheduleAfterResult(Match match)
    {
        if (HasResult(match))
        {
            match.DisciplinePhase.CompetitionDiscipline.IsScheduleLocked = true;
        }
    }

    private static void ValidateMainScore(Match match, int? homeScore, int? awayScore)
    {
        var discipline = match.DisciplinePhase.CompetitionDiscipline;
        if (!discipline.UsesSetScores || homeScore is null || awayScore is null)
        {
            return;
        }

        var setsToWin = discipline.SetsToWin
            ?? throw new ValidationException("U disciplíny není nastaven počet vítězných setů.");
        var isValid = (homeScore == setsToWin && awayScore >= 0 && awayScore < setsToWin) ||
            (awayScore == setsToWin && homeScore >= 0 && homeScore < setsToWin);
        if (!isValid)
        {
            throw new ValidationException(
                $"Hlavní výsledek musí mít vítěze s {setsToWin} vyhranými sety a poraženého s méně než {setsToWin} sety.");
        }
    }

    private static void ValidateSetResultConsistency(Match match, IReadOnlyList<SetResult> sets)
    {
        if (!match.DisciplinePhase.CompetitionDiscipline.UsesSetScores || sets.Count == 0 ||
            match.HomeScore is null || match.AwayScore is null)
        {
            return;
        }

        var expectedSetCount = match.HomeScore.Value + match.AwayScore.Value;
        var homeWins = sets.Count(x => x.HomeScore > x.AwayScore);
        var awayWins = sets.Count - homeWins;
        if (sets.Count != expectedSetCount || homeWins != match.HomeScore || awayWins != match.AwayScore)
        {
            throw new ValidationException("Výsledky jednotlivých setů neodpovídají hlavnímu výsledku zápasu.");
        }
    }

    private sealed record SetResult(int SetNumber, int HomeScore, int AwayScore);
    private sealed record RandomResultMatch(long Id, int Version, bool UsesSetScores, int? SetsToWin);
    private sealed record RandomMatchResult(int HomeScore, int AwayScore, List<MatchSetScoreInput> Sets);

    private async Task SaveMatchEditAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("Výsledek mezitím změnil někdo jiný. Obnovte stránku a zkuste to znovu.");
        }
    }

    private async Task UpdateKnockoutAdvancementAsync(Match sourceMatch, CancellationToken cancellationToken)
    {
        if (sourceMatch.DisciplinePhase.Type != PhaseType.Knockout)
        {
            return;
        }

        if (sourceMatch.HomeScore is not null && sourceMatch.HomeScore == sourceMatch.AwayScore)
        {
            throw new ValidationException("Vyřazovací zápas musí mít vítěze.");
        }

        var winnerTeamId = sourceMatch.HomeScore is null
            ? null
            : sourceMatch.HomeScore > sourceMatch.AwayScore ? sourceMatch.HomeTeamId : sourceMatch.AwayTeamId;
        var dependentMatches = await dbContext.Matches
            .Include(x => x.SetScores)
            .Where(x => x.HomeSourceMatchId == sourceMatch.Id || x.AwaySourceMatchId == sourceMatch.Id)
            .ToListAsync(cancellationToken);

        foreach (var dependentMatch in dependentMatches)
        {
            var currentTeamId = dependentMatch.HomeSourceMatchId == sourceMatch.Id
                ? dependentMatch.HomeTeamId
                : dependentMatch.AwayTeamId;
            if (currentTeamId == winnerTeamId)
            {
                continue;
            }
            if (dependentMatch.HomeScore is not null || dependentMatch.AwayScore is not null ||
                dependentMatch.SetScores.Count != 0 || dependentMatch.Status != MatchStatus.Scheduled)
            {
                throw new ValidationException("Výsledek nelze změnit, protože navazující zápas už má uložený výsledek.");
            }

            if (dependentMatch.HomeSourceMatchId == sourceMatch.Id)
            {
                dependentMatch.HomeTeamId = winnerTeamId;
            }
            else
            {
                dependentMatch.AwayTeamId = winnerTeamId;
            }
        }
    }

    private async Task ReconcileGroupStandingMatchesAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var isTwoGroupClassification = await dbContext.CompetitionDisciplines.AsNoTracking().AnyAsync(x =>
            x.Id == competitionDisciplineId &&
            x.CompetitionEditionId == editionId &&
            x.PlayingSystem == PlayingSystemType.GroupsThenClassificationMatches, cancellationToken);
        if (!isTwoGroupClassification)
        {
            return;
        }

        var homeSourceGroupIds = await dbContext.Matches.AsNoTracking()
            .Where(x =>
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.HomeSourceGroupId != null)
            .Select(x => x.HomeSourceGroupId!.Value)
            .ToListAsync(cancellationToken);
        var awaySourceGroupIds = await dbContext.Matches.AsNoTracking()
            .Where(x =>
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.AwaySourceGroupId != null)
            .Select(x => x.AwaySourceGroupId!.Value)
            .ToListAsync(cancellationToken);
        var sourceGroupIds = homeSourceGroupIds
            .Concat(awaySourceGroupIds)
            .Distinct()
            .ToList();
        if (sourceGroupIds.Count == 0)
        {
            return;
        }

        var completion = await dbContext.PhaseGroups.AsNoTracking()
            .Where(x => sourceGroupIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                TeamCount = x.Teams.Count,
                MatchCount = x.Matches.Count,
                CompletedMatchCount = x.Matches.Count(match =>
                    match.Status == MatchStatus.Completed &&
                    match.HomeTeamId != null &&
                    match.AwayTeamId != null &&
                    match.HomeScore != null &&
                    match.AwayScore != null)
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var standings = await new GroupStandingsService(dbContext)
            .GetForDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        var settledSlots = standings
            .Where(table =>
                completion.TryGetValue(table.GroupId, out var group) &&
                group.TeamCount >= 2 &&
                group.MatchCount == group.TeamCount * (group.TeamCount - 1) / 2 &&
                group.CompletedMatchCount == group.MatchCount)
            .SelectMany(table => table.Rows.Select(row => new
            {
                table.GroupId,
                row.Position,
                row.TeamId
            }))
            .ToDictionary(x => (x.GroupId, x.Position), x => x.TeamId);

        var matches = await dbContext.Matches
            .Include(x => x.SetScores)
            .Where(x =>
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                (x.HomeSourceGroupId != null || x.AwaySourceGroupId != null))
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var match in matches)
        {
            changed |= ReconcileGroupStandingSlot(match, settledSlots, home: true);
            changed |= ReconcileGroupStandingSlot(match, settledSlots, home: false);
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool ReconcileGroupStandingSlot(
        Match match,
        IReadOnlyDictionary<(long GroupId, int Position), long> settledSlots,
        bool home)
    {
        var sourceGroupId = home ? match.HomeSourceGroupId : match.AwaySourceGroupId;
        var sourceRank = home ? match.HomeSourceRank : match.AwaySourceRank;
        if (sourceGroupId is null || sourceRank is null)
        {
            return false;
        }

        long? desiredTeamId = settledSlots.TryGetValue((sourceGroupId.Value, sourceRank.Value), out var settledTeamId)
            ? settledTeamId
            : null;
        var currentTeamId = home ? match.HomeTeamId : match.AwayTeamId;
        if (currentTeamId == desiredTeamId || HasResult(match))
        {
            return false;
        }

        if (home)
        {
            match.HomeTeamId = desiredTeamId;
        }
        else
        {
            match.AwayTeamId = desiredTeamId;
        }
        match.Version++;
        match.UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private async Task ReconcileKnockoutAdvancementAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var isKnockout = await dbContext.CompetitionDisciplines.AnyAsync(x =>
            x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId &&
            x.PlayingSystem == PlayingSystemType.Knockout, cancellationToken);
        if (!isKnockout)
        {
            return;
        }

        var matches = await dbContext.Matches
            .Where(x => x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
        var matchesById = matches.ToDictionary(x => x.Id);
        var changed = false;

        foreach (var match in matches)
        {
            changed |= ReconcileSlot(match, true);
            changed |= ReconcileSlot(match, false);
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        bool ReconcileSlot(Match targetMatch, bool home)
        {
            var sourceMatchId = home ? targetMatch.HomeSourceMatchId : targetMatch.AwaySourceMatchId;
            if (sourceMatchId is null || !matchesById.TryGetValue(sourceMatchId.Value, out var sourceMatch) ||
                sourceMatch.HomeScore is null || sourceMatch.AwayScore is null ||
                sourceMatch.HomeScore == sourceMatch.AwayScore)
            {
                return false;
            }

            var winnerTeamId = sourceMatch.HomeScore > sourceMatch.AwayScore
                ? sourceMatch.HomeTeamId
                : sourceMatch.AwayTeamId;
            if (winnerTeamId is null)
            {
                return false;
            }

            var currentTeamId = home ? targetMatch.HomeTeamId : targetMatch.AwayTeamId;
            if (currentTeamId == winnerTeamId)
            {
                return false;
            }
            if (targetMatch.HomeScore is not null || targetMatch.AwayScore is not null ||
                targetMatch.Status != MatchStatus.Scheduled)
            {
                return false;
            }

            if (home)
            {
                targetMatch.HomeTeamId = winnerTeamId;
            }
            else
            {
                targetMatch.AwayTeamId = winnerTeamId;
            }
            return true;
        }
    }

    internal static IReadOnlyList<(int Round, long HomeTeamId, long AwayTeamId)> CreateBergerPairings(IReadOnlyList<long> teamIds)
    {
        var originalIndex = teamIds.Select((teamId, index) => (teamId, index))
            .ToDictionary(item => item.teamId, item => item.index);
        var orientationModulus = teamIds.Count % 2 == 0 ? teamIds.Count + 1 : teamIds.Count;
        var homeHalf = (orientationModulus - 1) / 2;
        var rotation = teamIds.Cast<long?>().ToList();
        if (rotation.Count % 2 != 0)
        {
            rotation.Add(null);
        }

        var result = new List<(int, long, long)>();
        for (var round = 0; round < rotation.Count - 1; round++)
        {
            for (var index = 0; index < rotation.Count / 2; index++)
            {
                var first = rotation[index];
                var second = rotation[rotation.Count - 1 - index];
                if (first is null || second is null)
                {
                    continue;
                }

                var firstIndex = originalIndex[first.Value];
                var secondIndex = originalIndex[second.Value];
                var distance = (secondIndex - firstIndex + orientationModulus) % orientationModulus;
                var firstIsHome = distance <= homeHalf;
                result.Add((round + 1,
                    firstIsHome ? first.Value : second.Value,
                    firstIsHome ? second.Value : first.Value));
            }

            var last = rotation[^1];
            rotation.RemoveAt(rotation.Count - 1);
            rotation.Insert(1, last);
        }
        return result;
    }

    private int AddBergerMatches(long phaseId, long groupId, string groupName, IReadOnlyList<long> teamIds)
    {
        var pairings = CreateBergerPairings(teamIds);
        var matchInRound = new Dictionary<int, int>();
        for (var index = 0; index < pairings.Count; index++)
        {
            var pairing = pairings[index];
            matchInRound[pairing.Round] = matchInRound.GetValueOrDefault(pairing.Round) + 1;
            dbContext.Matches.Add(new Match
            {
                DisciplinePhaseId = phaseId,
                PhaseGroupId = groupId,
                HomeTeamId = pairing.HomeTeamId,
                AwayTeamId = pairing.AwayTeamId,
                Name = $"{groupName} – {pairing.Round}. kolo, zápas {matchInRound[pairing.Round]}",
                Order = index + 1,
                Status = MatchStatus.Scheduled
            });
        }
        return pairings.Count;
    }

    private static void EnsureRoundRobinCanBeGenerated(int teamCount)
    {
        if (teamCount < 2)
        {
            throw new ValidationException("Pro rozpis musí být ve skupině alespoň dva týmy.");
        }
    }

    private async Task<bool> EnsurePresetAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var discipline = await dbContext.CompetitionDisciplines
            .Include(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Phases).ThenInclude(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            return false;
        }

        if (discipline.IsClosed)
        {
            return true;
        }

        var canRebuildPreset = !await dbContext.Matches.AnyAsync(
            x => x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                (x.Status != MatchStatus.Scheduled || x.HomeScore != null || x.AwayScore != null || x.SetScores.Any()),
            cancellationToken);

        if (discipline.PlayingSystem == PlayingSystemType.RoundRobin)
        {
            if (!HasRoundRobinPreset(discipline) && canRebuildPreset)
            {
                await ClearPhasesAsync(discipline, cancellationToken);
                var phase = CreatePresetPhase(discipline.Id, "Detaily", PhaseType.Group, 1);
                var presetGroup = new PhaseGroup { Name = "Každý s každým", Order = 1 };
                phase.Groups.Add(presetGroup);
                foreach (var team in discipline.Teams.OrderBy(x => x.Seed))
                {
                    presetGroup.Teams.Add(new PhaseGroupTeam
                    {
                        DisciplineTeamId = team.Id,
                        Seed = presetGroup.Teams.Count + 1
                    });
                }
                dbContext.DisciplinePhases.Add(phase);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            var group = discipline.Phases.SelectMany(x => x.Groups).SingleOrDefault();
            if (group is not null && !discipline.Phases.SelectMany(x => x.Matches).Any())
            {
                var assigned = group.Teams.Select(x => x.DisciplineTeamId).ToHashSet();
                foreach (var team in discipline.Teams.OrderBy(x => x.Seed).Where(x => !assigned.Contains(x.Id)))
                {
                    group.Teams.Add(new PhaseGroupTeam
                    {
                        DisciplinePhaseId = group.DisciplinePhaseId,
                        DisciplineTeamId = team.Id,
                        Seed = group.Teams.Count + 1
                    });
                }
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        else if (discipline.PlayingSystem == PlayingSystemType.GroupsThenClassificationMatches &&
                 !HasTwoGroupPreset(discipline) && canRebuildPreset)
        {
            await ClearPhasesAsync(discipline, cancellationToken);
            var groupA = CreatePresetPhase(discipline.Id, "Skupina A", PhaseType.Group, 1);
            groupA.Groups.Add(new PhaseGroup { Name = "Skupina A", Order = 1 });
            var groupB = CreatePresetPhase(discipline.Id, "Skupina B", PhaseType.Group, 2);
            groupB.Groups.Add(new PhaseGroup { Name = "Skupina B", Order = 1 });
            var final = CreatePresetPhase(discipline.Id, "O konečné umístění", PhaseType.FinalStanding, 3);
            dbContext.DisciplinePhases.AddRange(groupA, groupB, final);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (discipline.PlayingSystem == PlayingSystemType.Knockout &&
                 !HasKnockoutPreset(discipline) && canRebuildPreset)
        {
            await ClearPhasesAsync(discipline, cancellationToken);
            dbContext.DisciplinePhases.Add(CreatePresetPhase(discipline.Id, "Vyřazovací část", PhaseType.Knockout, 1));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task ClearPhasesAsync(CompetitionDiscipline discipline, CancellationToken cancellationToken)
    {
        var phases = discipline.Phases.ToList();
        dbContext.Matches.RemoveRange(phases.SelectMany(x => x.Matches));
        dbContext.PhaseGroupTeams.RemoveRange(phases.SelectMany(x => x.Groups).SelectMany(x => x.Teams));
        dbContext.PhaseGroups.RemoveRange(phases.SelectMany(x => x.Groups));
        dbContext.DisciplinePhases.RemoveRange(phases);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DisciplinePhase CreatePresetPhase(long disciplineId, string name, PhaseType type, int order) => new()
    {
        CompetitionDisciplineId = disciplineId,
        Name = name,
        Type = type,
        Order = order,
        PointsForWin = 2,
        PointsForDraw = 1,
        PointsForLoss = 0
    };

    private static bool HasRoundRobinPreset(CompetitionDiscipline discipline) =>
        discipline.Phases.Count == 1 && discipline.Phases.Single().Type == PhaseType.Group &&
        discipline.Phases.Single().Groups.Count == 1;

    private static bool HasTwoGroupPreset(CompetitionDiscipline discipline) =>
        discipline.Phases.Count == 3 && discipline.Phases.Count(x => x.Type == PhaseType.Group) == 2 &&
        discipline.Phases.Count(x => x.Type == PhaseType.FinalStanding) == 1 &&
        discipline.Phases.Where(x => x.Type == PhaseType.Group).All(x => x.Groups.Count == 1);

    private static bool HasKnockoutPreset(CompetitionDiscipline discipline) =>
        discipline.Phases.Count == 1 && discipline.Phases.Single().Type == PhaseType.Knockout;

    private async Task EnsureDisciplineAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var discipline = await dbContext.CompetitionDisciplines.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (discipline is null)
        {
            throw new ValidationException("Disciplína neexistuje.");
        }
        EnsureOpen(discipline);
    }

    private async Task<DisciplinePhase> GetPhaseAsync(long editionId, long competitionDisciplineId, long phaseId, CancellationToken cancellationToken)
    {
        var phase = await dbContext.DisciplinePhases.Include(x => x.CompetitionDiscipline)
            .SingleOrDefaultAsync(x => x.Id == phaseId && x.CompetitionDisciplineId == competitionDisciplineId &&
                x.CompetitionDiscipline.CompetitionEditionId == editionId, cancellationToken)
            ?? throw new ValidationException("Fáze neexistuje.");
        EnsureOpen(phase.CompetitionDiscipline);
        return phase;
    }

    private async Task<PhaseGroup> GetGroupAsync(long editionId, long competitionDisciplineId, long phaseId, long groupId, CancellationToken cancellationToken)
    {
        var group = await dbContext.PhaseGroups.Include(x => x.DisciplinePhase).ThenInclude(x => x.CompetitionDiscipline)
            .SingleOrDefaultAsync(x => x.Id == groupId && x.DisciplinePhaseId == phaseId &&
                x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId &&
                x.DisciplinePhase.CompetitionDiscipline.CompetitionEditionId == editionId, cancellationToken)
            ?? throw new ValidationException("Skupina nebo etapa neexistuje.");
        EnsureOpen(group.DisciplinePhase.CompetitionDiscipline);
        return group;
    }

    private static void EnsureOpen(CompetitionDiscipline discipline)
    {
        if (discipline.IsClosed)
        {
            throw new ValidationException("Uzavřenou disciplínu už nelze měnit.");
        }
    }

    private static void Validate(object input)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
        {
            throw new ValidationException(results[0].ErrorMessage);
        }
    }

    private sealed record BracketSource(long? TeamId, long? MatchId);
}
