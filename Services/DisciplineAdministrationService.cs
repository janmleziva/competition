using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class DisciplineAdministrationService(CompetitionDbContext dbContext)
    : IDisciplineAdministrationService
{
    public async Task<IReadOnlyList<DisciplineCatalogItem>> ListCatalogAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Disciplines.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new DisciplineCatalogItem(x.Id, x.Name, x.CompetitionDisciplines.Any()))
            .ToListAsync(cancellationToken);

    public async Task<long> CreateCatalogAsync(DisciplineCatalogInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var name = input.Name.Trim();
        if (await dbContext.Disciplines.AnyAsync(x => x.Name == name, cancellationToken))
        {
            throw new ValidationException("Disciplína s tímto názvem už v katalogu je.");
        }

        var discipline = new Discipline { Name = name };
        dbContext.Disciplines.Add(discipline);
        await dbContext.SaveChangesAsync(cancellationToken);
        return discipline.Id;
    }

    public async Task<bool> RenameCatalogAsync(long id, DisciplineCatalogInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var discipline = await dbContext.Disciplines.FindAsync([id], cancellationToken);
        if (discipline is null)
        {
            return false;
        }

        var name = input.Name.Trim();
        if (await dbContext.Disciplines.AnyAsync(x => x.Id != id && x.Name == name, cancellationToken))
        {
            throw new ValidationException("Disciplína s tímto názvem už v katalogu je.");
        }

        discipline.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCatalogAsync(long id, CancellationToken cancellationToken = default)
    {
        var discipline = await dbContext.Disciplines
            .Include(x => x.CompetitionDisciplines)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (discipline is null)
        {
            return false;
        }

        if (discipline.CompetitionDisciplines.Count != 0)
        {
            throw new ValidationException("Disciplínu nelze smazat, protože je přiřazena k ročníku.");
        }

        dbContext.Disciplines.Remove(discipline);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EditionDisciplineSetup?> GetEditionSetupAsync(long editionId, CancellationToken cancellationToken = default)
    {
        var edition = await dbContext.CompetitionEditions.AsNoTracking()
            .Where(x => x.Id == editionId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (edition is null)
        {
            return null;
        }

        var configured = await dbContext.CompetitionDisciplines.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Order)
            .Select(x => new ConfiguredDisciplineItem(
                x.Id,
                x.DisciplineId,
                x.Discipline.Name,
                x.Order,
                x.PlayingSystem,
                x.TeamSize,
                x.ScheduledAt,
                x.UsesSetScores,
                x.Teams.Count,
                x.ParticipantAssignments.Count,
                x.Description,
                x.SetsToWin,
                x.IsLocked,
                x.Phases.SelectMany(p => p.Matches).Any(m =>
                    m.HomeScore != null || m.AwayScore != null || m.SetScores.Any() || m.Status != MatchStatus.Scheduled),
                x.IsScheduleLocked,
                x.Phases.SelectMany(p => p.Matches).Any(),
                x.Phases.Any(),
                x.Phases.SelectMany(p => p.Groups).SelectMany(g => g.Teams).Any(),
                x.IsClosed,
                x.AwardPointSystemId,
                x.AwardPointSystem == null ? null : x.AwardPointSystem.Name,
                x.FinalStandings.OrderBy(s => s.Rank).Select(s => new DisciplineAwardedStanding(
                    s.Rank,
                    s.DisciplineTeamId,
                    string.Join("/", s.DisciplineTeam.Members.OrderBy(m => m.Order)
                        .Select(m => m.CompetitionEntry.Competitor.LastName)),
                    s.PointsAwarded)).ToList(),
                x.Phases.SelectMany(p => p.Matches).Any() &&
                    !x.Phases.SelectMany(p => p.Matches).Any(m =>
                        m.Status != MatchStatus.Completed || m.HomeScore == null || m.AwayScore == null),
                x.AwardPointSystem == null
                    ? null
                    : x.AwardPointSystem.Rules.OrderBy(rule => rule.Rank)
                        .Select(rule => new AwardPointRuleItem(rule.Rank, rule.Points))
                        .ToList()))
            .ToListAsync(cancellationToken);

        var teams = await dbContext.DisciplineTeams.AsNoTrackingWithIdentityResolution()
            .Where(team => team.CompetitionDiscipline.CompetitionEditionId == editionId)
            .Include(team => team.Members).ThenInclude(member => member.CompetitionEntry)
                .ThenInclude(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var teamsByDiscipline = teams.GroupBy(team => team.CompetitionDisciplineId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var editionEntries = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(entry => entry.CompetitionEditionId == editionId)
            .Include(entry => entry.Competitor)
            .ToListAsync(cancellationToken);
        var entryLabels = TeamNameFormatter.CreateEntryLabels(editionEntries);
        configured = configured.Select(item =>
        {
            if (item.FinalStandings is not { Count: > 0 } ||
                !teamsByDiscipline.TryGetValue(item.Id, out var disciplineTeams))
            {
                return item;
            }

            var teamById = disciplineTeams.ToDictionary(team => team.Id);
            return item with
            {
                FinalStandings = item.FinalStandings.Select(standing =>
                    teamById.TryGetValue(standing.TeamId, out var team)
                        ? standing with { TeamName = TeamNameFormatter.Format(team, entryLabels) }
                        : standing).ToList()
            };
        }).ToList();

        var usedIds = configured.Select(x => x.DisciplineId).ToHashSet();
        var catalog = (await ListCatalogAsync(cancellationToken)).Where(x => !usedIds.Contains(x.Id)).ToList();
        return new EditionDisciplineSetup(edition.Id, edition.Name, catalog, configured);
    }

    public async Task<EditionDisciplineDetail?> GetDetailAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var setup = await GetEditionSetupAsync(editionId, cancellationToken);
        var discipline = setup?.Disciplines.SingleOrDefault(x => x.Id == competitionDisciplineId);
        return setup is null || discipline is null
            ? null
            : new EditionDisciplineDetail(setup.EditionId, setup.EditionName, discipline);
    }

    public async Task<DisciplineParticipantSetup?> GetParticipantSetupAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var setup = await GetEditionSetupAsync(editionId, cancellationToken);
        var discipline = setup?.Disciplines.SingleOrDefault(x => x.Id == competitionDisciplineId);
        if (setup is null || discipline is null)
        {
            return null;
        }

        var selectedIds = (await dbContext.DisciplineParticipantAssignments.AsNoTracking()
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .Select(x => x.CompetitionEntryId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var teamMemberIds = (await dbContext.DisciplineTeamMembers.AsNoTracking()
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .Select(x => x.CompetitionEntryId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var participants = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Seed)
            .Select(x => new DisciplineParticipant(
                x.Id,
                x.Seed,
                x.Competitor.FirstName,
                x.Competitor.LastName,
                selectedIds.Contains(x.Id),
                teamMemberIds.Contains(x.Id)))
            .ToListAsync(cancellationToken);

        var teams = await dbContext.DisciplineTeams.AsNoTracking()
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(x => x.Seed)
            .Select(x => new DisciplineTeamItem(
                x.Id,
                x.Seed,
                x.Members.OrderBy(m => m.Order)
                    .Select(m => new DisciplineTeamMemberItem(
                        m.CompetitionEntryId,
                        m.CompetitionEntry.Seed,
                        m.CompetitionEntry.Competitor.FirstName,
                        m.CompetitionEntry.Competitor.LastName,
                        m.Order))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var availableForTeams = participants
            .Where(x => x.IsAssigned && !x.IsInTeam)
            .ToList();

        return new DisciplineParticipantSetup(
            editionId,
            setup.EditionName,
            discipline,
            participants,
            teams,
            availableForTeams);
    }

    public async Task<long> AttachAsync(long editionId, EditionDisciplineInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);

        if (!dbContext.Database.IsRelational())
        {
            return await AttachCoreAsync(editionId, input, cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await AttachCoreAsync(editionId, input, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<long> AttachCoreAsync(
        long editionId,
        EditionDisciplineInput input,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.CompetitionEditions.AnyAsync(x => x.Id == editionId, cancellationToken))
        {
            throw new ValidationException("Ročník neexistuje.");
        }

        if (!await dbContext.Disciplines.AnyAsync(x => x.Id == input.DisciplineId, cancellationToken))
        {
            throw new ValidationException("Disciplína neexistuje.");
        }

        if (input.AwardPointSystemId is not null &&
            !await dbContext.AwardPointSystems.AnyAsync(x => x.Id == input.AwardPointSystemId, cancellationToken))
        {
            throw new ValidationException("Vybraný bodovací systém neexistuje.");
        }

        if (await dbContext.CompetitionDisciplines.AnyAsync(x => x.CompetitionEditionId == editionId && x.DisciplineId == input.DisciplineId, cancellationToken))
        {
            throw new ValidationException("Disciplína už je k tomuto ročníku přiřazena.");
        }

        var conflictingItem = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(
                x => x.CompetitionEditionId == editionId && x.Order == input.Order,
                cancellationToken);

        if (conflictingItem is not null)
        {
            var usedOrders = await dbContext.CompetitionDisciplines
                .Where(x => x.CompetitionEditionId == editionId)
                .Select(x => x.Order)
                .ToListAsync(cancellationToken);
            conflictingItem.Order = FindLowestFreePositiveInteger(usedOrders);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var item = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId,
            DisciplineId = input.DisciplineId,
            Order = input.Order,
            TeamSize = input.TeamSize,
            PlayingSystem = input.PlayingSystem,
            UsesSetScores = input.UsesSetScores,
            SetsToWin = input.UsesSetScores ? input.SetsToWin : null,
            AwardPointSystemId = input.AwardPointSystemId,
            Description = NormalizeDescription(input.Description),
            ScheduledAt = input.ScheduledAt
        };
        dbContext.CompetitionDisciplines.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<bool> UpdateAsync(long editionId, long competitionDisciplineId, EditionDisciplineInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);

        if (!dbContext.Database.IsRelational())
        {
            return await UpdateCoreAsync(editionId, competitionDisciplineId, input, cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await UpdateCoreAsync(editionId, competitionDisciplineId, input, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<bool> UpdateCoreAsync(
        long editionId,
        long competitionDisciplineId,
        EditionDisciplineInput input,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.CompetitionDisciplines
            .Include(x => x.ParticipantAssignments)
            .Include(x => x.Teams)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        EnsureOpen(item);
        if (item.IsLocked)
        {
            throw new ValidationException("Nastavení disciplíny je uzamčené. Nejprve disciplínu odemkněte.");
        }

        if (input.DisciplineId != item.DisciplineId)
        {
            throw new ValidationException("Katalogovou disciplínu při úpravě nelze změnit.");
        }

        if (item.Teams.Count != 0 && input.TeamSize != item.TeamSize)
        {
            throw new ValidationException("Velikost týmu nelze změnit, pokud už jsou vytvořené týmy.");
        }

        var hasMatches = await dbContext.Matches.AnyAsync(
            x => x.DisciplinePhase.CompetitionDisciplineId == item.Id,
            cancellationToken);
        var hasResults = await dbContext.Matches.AnyAsync(x =>
            x.DisciplinePhase.CompetitionDisciplineId == item.Id &&
            (x.HomeScore != null || x.AwayScore != null || x.SetScores.Any() || x.Status != MatchStatus.Scheduled),
            cancellationToken);
        var playingSystemChanged = input.PlayingSystem != item.PlayingSystem;

        if (playingSystemChanged && item.IsScheduleLocked)
        {
            throw new ValidationException("Herní systém nelze změnit, dokud je rozpis uzamčený.");
        }

        if (playingSystemChanged && hasMatches)
        {
            throw new ValidationException("Herní systém nelze změnit po vytvoření zápasů.");
        }

        if (hasResults && (input.UsesSetScores != item.UsesSetScores || input.SetsToWin != item.SetsToWin))
        {
            throw new ValidationException("Nastavení setů nelze změnit, protože disciplína už obsahuje výsledky.");
        }

        var conflictingItem = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(
                x => x.CompetitionEditionId == editionId && x.Order == input.Order && x.Id != item.Id,
                cancellationToken);

        if (conflictingItem is not null)
        {
            var originalOrder = item.Order;
            var temporaryOrder = await dbContext.CompetitionDisciplines
                .Where(x => x.CompetitionEditionId == editionId)
                .MaxAsync(x => x.Order, cancellationToken) + 1;

            conflictingItem.Order = temporaryOrder;
            await dbContext.SaveChangesAsync(cancellationToken);

            item.Order = input.Order;
            await dbContext.SaveChangesAsync(cancellationToken);

            conflictingItem.Order = originalOrder;
            item.TeamSize = input.TeamSize;
            item.PlayingSystem = input.PlayingSystem;
            item.UsesSetScores = input.UsesSetScores;
            item.SetsToWin = input.UsesSetScores ? input.SetsToWin : null;
            item.Description = NormalizeDescription(input.Description);
            item.ScheduledAt = input.ScheduledAt;
            if (playingSystemChanged)
            {
                await ClearPhaseSetupAsync(item.Id, cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        item.Order = input.Order;
        item.TeamSize = input.TeamSize;
        item.PlayingSystem = input.PlayingSystem;
        item.UsesSetScores = input.UsesSetScores;
        item.SetsToWin = input.UsesSetScores ? input.SetsToWin : null;
        item.Description = NormalizeDescription(input.Description);
        item.ScheduledAt = input.ScheduledAt;
        if (playingSystemChanged)
        {
            await ClearPhaseSetupAsync(item.Id, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ClearPhaseSetupAsync(long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var phases = await dbContext.DisciplinePhases
            .Include(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Matches).ThenInclude(x => x.SetScores)
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .ToListAsync(cancellationToken);

        dbContext.MatchSetScores.RemoveRange(phases.SelectMany(x => x.Matches).SelectMany(x => x.SetScores));
        dbContext.Matches.RemoveRange(phases.SelectMany(x => x.Matches));
        dbContext.PhaseGroupTeams.RemoveRange(phases.SelectMany(x => x.Groups).SelectMany(x => x.Teams));
        dbContext.PhaseGroups.RemoveRange(phases.SelectMany(x => x.Groups));
        dbContext.DisciplinePhases.RemoveRange(phases);
        dbContext.DisciplineStandings.RemoveRange(await dbContext.DisciplineStandings
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .ToListAsync(cancellationToken));
    }

    public async Task<bool> SetLockAsync(long editionId, long competitionDisciplineId, bool isLocked, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.CompetitionDisciplines
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (item is null)
        {
            return false;
        }
        EnsureOpen(item);

        item.IsLocked = isLocked;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.CompetitionDisciplines
            .Include(x => x.ParticipantAssignments)
            .Include(x => x.Teams)
            .ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        EnsureOpen(item);

        var hasResults = await dbContext.Matches.AnyAsync(x =>
            x.DisciplinePhase.CompetitionDisciplineId == item.Id &&
            (x.HomeScore != null || x.AwayScore != null || x.SetScores.Any() || x.Status != MatchStatus.Scheduled),
            cancellationToken);
        if (hasResults)
        {
            throw new ValidationException("Disciplínu nelze odebrat, protože už jsou odehrané zápasy.");
        }
        if (item.IsScheduleLocked)
        {
            throw new ValidationException("Disciplínu nelze odebrat, protože její rozpis je uzamčený.");
        }

        var phases = await dbContext.DisciplinePhases
            .Include(x => x.Groups).ThenInclude(x => x.Teams)
            .Include(x => x.Matches).ThenInclude(x => x.SetScores)
            .Where(x => x.CompetitionDisciplineId == item.Id)
            .ToListAsync(cancellationToken);
        dbContext.MatchSetScores.RemoveRange(phases.SelectMany(x => x.Matches).SelectMany(x => x.SetScores));
        dbContext.Matches.RemoveRange(phases.SelectMany(x => x.Matches));
        dbContext.PhaseGroupTeams.RemoveRange(phases.SelectMany(x => x.Groups).SelectMany(x => x.Teams));
        dbContext.PhaseGroups.RemoveRange(phases.SelectMany(x => x.Groups));
        dbContext.DisciplinePhases.RemoveRange(phases);
        dbContext.DisciplineStandings.RemoveRange(await dbContext.DisciplineStandings.Where(x => x.CompetitionDisciplineId == item.Id).ToListAsync(cancellationToken));
        dbContext.DisciplineParticipantAssignments.RemoveRange(item.ParticipantAssignments);
        dbContext.DisciplineTeamMembers.RemoveRange(item.Teams.SelectMany(x => x.Members));
        dbContext.DisciplineTeams.RemoveRange(item.Teams);
        dbContext.CompetitionDisciplines.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignAllAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var ids = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Seed)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return await UpdateParticipantsAsync(editionId, competitionDisciplineId, ids, cancellationToken);
    }

    public async Task<bool> UpdateParticipantsAsync(long editionId, long competitionDisciplineId, IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        var item = await LoadEditableDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var distinctIds = entryIds.Distinct().ToList();
        var validIds = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId && distinctIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validIds.Count != distinctIds.Count)
        {
            throw new ValidationException("Některý vybraný soutěžící není přihlášen do tohoto ročníku.");
        }

        var selectedSet = distinctIds.ToHashSet();
        var removedAssignments = item.ParticipantAssignments.Where(x => !selectedSet.Contains(x.CompetitionEntryId)).ToList();
        if (removedAssignments.Count != 0)
        {
            dbContext.DisciplineParticipantAssignments.RemoveRange(removedAssignments);
        }

        var missingIds = distinctIds.Except(item.ParticipantAssignments.Select(x => x.CompetitionEntryId)).ToList();
        foreach (var missingId in missingIds)
        {
            dbContext.DisciplineParticipantAssignments.Add(new DisciplineParticipantAssignment
            {
                CompetitionDisciplineId = competitionDisciplineId,
                CompetitionEntryId = missingId
            });
        }

        var removedMembers = item.Teams
            .SelectMany(x => x.Members)
            .Where(x => !selectedSet.Contains(x.CompetitionEntryId))
            .ToList();
        if (removedMembers.Count != 0)
        {
            dbContext.DisciplineTeamMembers.RemoveRange(removedMembers);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RemoveEmptyTeamsAndNormalizeAsync(competitionDisciplineId, cancellationToken);

        if (item.TeamSize == 1)
        {
            await RebuildSingleMemberTeamsAsync(competitionDisciplineId, editionId, selectedSet, cancellationToken);
        }

        return true;
    }

    public async Task<bool> CreateTeamAsync(long editionId, long competitionDisciplineId, IReadOnlyList<long> entryIds, CancellationToken cancellationToken = default)
    {
        var item = await LoadEditableDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        if (item.TeamSize <= 1)
        {
            throw new ValidationException("Ruční skládání týmů je dostupné jen pro týmové disciplíny.");
        }

        var distinctIds = entryIds.Where(x => x > 0).Distinct().ToList();
        if (distinctIds.Count != item.TeamSize)
        {
            throw new ValidationException($"Tým musí mít přesně {item.TeamSize} členy.");
        }

        var selectedIds = item.ParticipantAssignments.Select(x => x.CompetitionEntryId).ToHashSet();
        if (distinctIds.Any(x => !selectedIds.Contains(x)))
        {
            throw new ValidationException("Do týmu lze zařadit jen přiřazené účastníky disciplíny.");
        }

        var existingTeamMemberIds = item.Teams.SelectMany(x => x.Members).Select(x => x.CompetitionEntryId).ToHashSet();
        if (distinctIds.Any(existingTeamMemberIds.Contains))
        {
            throw new ValidationException("Některý vybraný soutěžící už je v jiném týmu.");
        }

        var team = new DisciplineTeam
        {
            CompetitionDisciplineId = competitionDisciplineId,
            Seed = item.Teams.Count == 0 ? 1 : item.Teams.Max(x => x.Seed) + 1
        };

        for (var index = 0; index < distinctIds.Count; index++)
        {
            team.Members.Add(new DisciplineTeamMember
            {
                CompetitionDisciplineId = competitionDisciplineId,
                CompetitionEntryId = distinctIds[index],
                Order = index + 1
            });
        }

        dbContext.DisciplineTeams.Add(team);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RandomizeTeamsAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken = default)
    {
        var item = await LoadEditableDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        if (item.TeamSize <= 1)
        {
            throw new ValidationException("Losování týmů je dostupné jen pro týmové disciplíny.");
        }

        var selectedIds = item.ParticipantAssignments.Select(x => x.CompetitionEntryId).ToList();
        if (selectedIds.Count == 0)
        {
            throw new ValidationException("Nejprve přiřaďte účastníky do disciplíny.");
        }

        if (selectedIds.Count % item.TeamSize != 0)
        {
            throw new ValidationException($"Počet přiřazených účastníků musí být dělitelný velikostí týmu ({item.TeamSize}).");
        }

        await RemovePhaseAssignmentsForTeamsAsync(item.Teams.Select(x => x.Id), cancellationToken);
        dbContext.DisciplineTeamMembers.RemoveRange(item.Teams.SelectMany(x => x.Members));
        dbContext.DisciplineTeams.RemoveRange(item.Teams);
        await dbContext.SaveChangesAsync(cancellationToken);

        var randomized = selectedIds.OrderBy(_ => Random.Shared.Next()).ToList();
        for (var offset = 0; offset < randomized.Count; offset += item.TeamSize)
        {
            var team = new DisciplineTeam
            {
                CompetitionDisciplineId = competitionDisciplineId,
                Seed = offset / item.TeamSize + 1
            };

            for (var memberIndex = 0; memberIndex < item.TeamSize; memberIndex++)
            {
                team.Members.Add(new DisciplineTeamMember
                {
                    CompetitionDisciplineId = competitionDisciplineId,
                    CompetitionEntryId = randomized[offset + memberIndex],
                    Order = memberIndex + 1
                });
            }

            dbContext.DisciplineTeams.Add(team);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTeamAsync(long editionId, long competitionDisciplineId, long teamId, CancellationToken cancellationToken = default)
    {
        var item = await LoadEditableDisciplineAsync(editionId, competitionDisciplineId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var team = item.Teams.SingleOrDefault(x => x.Id == teamId);
        if (team is null)
        {
            return false;
        }

        await RemovePhaseAssignmentsForTeamsAsync([team.Id], cancellationToken);
        dbContext.DisciplineTeamMembers.RemoveRange(team.Members);
        dbContext.DisciplineTeams.Remove(team);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NormalizeTeamSeedsAsync(competitionDisciplineId, cancellationToken);
        return true;
    }

    private async Task<CompetitionDiscipline?> LoadEditableDisciplineAsync(long editionId, long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var item = await dbContext.CompetitionDisciplines
            .Include(x => x.ParticipantAssignments)
            .Include(x => x.Teams)
            .ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(x => x.Id == competitionDisciplineId && x.CompetitionEditionId == editionId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        EnsureOpen(item);

        if (item.IsScheduleLocked)
        {
            throw new ValidationException("Účastníky a týmy nelze měnit, dokud je rozpis uzamčený.");
        }

        var hasMatches = await dbContext.Matches.AsNoTracking()
            .AnyAsync(x => x.DisciplinePhase.CompetitionDisciplineId == competitionDisciplineId, cancellationToken);
        if (hasMatches)
        {
            throw new ValidationException("Účastníky a týmy nelze měnit, protože rozpis už obsahuje zápasy.");
        }

        return item;
    }

    private async Task RebuildSingleMemberTeamsAsync(long competitionDisciplineId, long editionId, HashSet<long> selectedSet, CancellationToken cancellationToken)
    {
        var existingTeams = await dbContext.DisciplineTeams
            .Include(x => x.Members)
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .ToListAsync(cancellationToken);
        await RemovePhaseAssignmentsForTeamsAsync(existingTeams.Select(x => x.Id), cancellationToken);
        dbContext.DisciplineTeamMembers.RemoveRange(existingTeams.SelectMany(x => x.Members));
        dbContext.DisciplineTeams.RemoveRange(existingTeams);
        await dbContext.SaveChangesAsync(cancellationToken);

        var orderedIds = await dbContext.CompetitionEntries.AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId && selectedSet.Contains(x.Id))
            .OrderBy(x => x.Seed)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < orderedIds.Count; index++)
        {
            var team = new DisciplineTeam
            {
                CompetitionDisciplineId = competitionDisciplineId,
                Seed = index + 1
            };
            team.Members.Add(new DisciplineTeamMember
            {
                CompetitionDisciplineId = competitionDisciplineId,
                CompetitionEntryId = orderedIds[index],
                Order = 1
            });
            dbContext.DisciplineTeams.Add(team);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RemoveEmptyTeamsAndNormalizeAsync(long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var teams = await dbContext.DisciplineTeams
            .Include(x => x.Members)
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .ToListAsync(cancellationToken);
        var emptyTeams = teams.Where(x => x.Members.Count == 0).ToList();
        if (emptyTeams.Count != 0)
        {
            await RemovePhaseAssignmentsForTeamsAsync(emptyTeams.Select(x => x.Id), cancellationToken);
            dbContext.DisciplineTeams.RemoveRange(emptyTeams);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await NormalizeTeamSeedsAsync(competitionDisciplineId, cancellationToken);
    }

    private async Task NormalizeTeamSeedsAsync(long competitionDisciplineId, CancellationToken cancellationToken)
    {
        var teams = await dbContext.DisciplineTeams
            .Include(x => x.Members)
            .Where(x => x.CompetitionDisciplineId == competitionDisciplineId)
            .OrderBy(x => x.Seed)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        for (var teamIndex = 0; teamIndex < teams.Count; teamIndex++)
        {
            teams[teamIndex].Seed = teamIndex + 1;
            var orderedMembers = teams[teamIndex].Members.OrderBy(x => x.Order).ThenBy(x => x.Id).ToList();
            for (var memberIndex = 0; memberIndex < orderedMembers.Count; memberIndex++)
            {
                orderedMembers[memberIndex].Order = memberIndex + 1;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RemovePhaseAssignmentsForTeamsAsync(IEnumerable<long> teamIds, CancellationToken cancellationToken)
    {
        var ids = teamIds.ToHashSet();
        if (ids.Count == 0)
        {
            return;
        }

        var assignments = await dbContext.PhaseGroupTeams
            .Where(x => ids.Contains(x.DisciplineTeamId))
            .ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return;
        }

        dbContext.PhaseGroupTeams.RemoveRange(assignments);
    }

    private static int FindLowestFreePositiveInteger(IEnumerable<int> values)
    {
        var used = values.Where(x => x > 0).ToHashSet();
        var candidate = 1;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static void Validate(object input)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
        {
            throw new ValidationException(results[0].ErrorMessage);
        }
    }

    private static void EnsureOpen(CompetitionDiscipline discipline)
    {
        if (discipline.IsClosed)
        {
            throw new ValidationException("Uzavřenou disciplínu už nelze měnit.");
        }
    }
}
