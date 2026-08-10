using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class CompetitorAdministrationService(CompetitionDbContext dbContext)
    : ICompetitorAdministrationService
{
    public async Task<IReadOnlyList<CompetitorSummary>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Competitors.AsNoTracking();
        var normalizedSearch = search?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                x.FirstName.Contains(normalizedSearch) ||
                x.LastName.Contains(normalizedSearch));
        }

        return await query
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.DateOfBirth)
            .Select(x => new CompetitorSummary(
                x.Id,
                x.FirstName,
                x.LastName,
                x.DateOfBirth,
                x.CompetitionEntries.Count))
            .ToListAsync(cancellationToken);
    }

    public Task<CompetitorDetails?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        dbContext.Competitors
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new CompetitorDetails(x.Id, x.FirstName, x.LastName, x.DateOfBirth))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<long> CreateAsync(
        CompetitorInput input,
        bool allowDuplicateName = false,
        CancellationToken cancellationToken = default)
    {
        Validate(input);
        await EnsureDuplicateNameIsConfirmedAsync(input, allowDuplicateName, cancellationToken);
        var competitor = CreateCompetitor(input);

        dbContext.Competitors.Add(competitor);
        await dbContext.SaveChangesAsync(cancellationToken);
        return competitor.Id;
    }

    public async Task<bool> UpdateAsync(
        long id,
        CompetitorInput input,
        CancellationToken cancellationToken = default)
    {
        Validate(input);
        var competitor = await dbContext.Competitors.FindAsync([id], cancellationToken);
        if (competitor is null)
        {
            return false;
        }

        competitor.FirstName = input.FirstName.Trim();
        competitor.LastName = input.LastName.Trim();
        competitor.DateOfBirth = input.DateOfBirth;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var competitor = await dbContext.Competitors.FindAsync([id], cancellationToken);
        if (competitor is null)
        {
            return false;
        }

        if (await dbContext.CompetitionEntries.AnyAsync(
                x => x.CompetitorId == id,
                cancellationToken))
        {
            throw new ValidationException("Zaregistrovaný soutěžící nemůže být smazán.");
        }

        dbContext.Competitors.Remove(competitor);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EditionRegistrationDetails?> GetEditionRegistrationAsync(
        long editionId,
        CancellationToken cancellationToken = default)
    {
        var edition = await dbContext.CompetitionEditions
            .AsNoTracking()
            .Where(x => x.Id == editionId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (edition is null)
        {
            return null;
        }

        var entries = await dbContext.CompetitionEntries
            .AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Seed)
            .Select(x => new CompetitionEntrySummary(
                x.Id,
                x.CompetitorId,
                x.Competitor.FirstName,
                x.Competitor.LastName,
                x.Competitor.DateOfBirth,
                x.Seed))
            .ToListAsync(cancellationToken);

        var registeredIds = entries.Select(x => x.CompetitorId).ToArray();
        var available = await dbContext.Competitors
            .AsNoTracking()
            .Where(x => !registeredIds.Contains(x.Id))
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Select(x => new CompetitorSummary(
                x.Id,
                x.FirstName,
                x.LastName,
                x.DateOfBirth,
                x.CompetitionEntries.Count))
            .ToListAsync(cancellationToken);

        return new EditionRegistrationDetails(edition.Id, edition.Name, entries, available);
    }

    public async Task RegisterAsync(
        long editionId,
        long competitorId,
        int seed,
        CancellationToken cancellationToken = default)
    {
        ValidateSeed(seed);

        if (!await dbContext.CompetitionEditions.AnyAsync(x => x.Id == editionId, cancellationToken))
        {
            throw new ValidationException("Soutěž nebyla nalezena.");
        }

        if (!await dbContext.Competitors.AnyAsync(x => x.Id == competitorId, cancellationToken))
        {
            throw new ValidationException("Soutěžící nebyl nalezen.");
        }

        if (await dbContext.CompetitionEntries.AnyAsync(
                x => x.CompetitionEditionId == editionId && x.CompetitorId == competitorId,
                cancellationToken))
        {
            throw new ValidationException("Tento soutěžící už je do soutěže přihlášen.");
        }

        await EnsureSeedIsAvailableAsync(editionId, seed, null, cancellationToken);

        dbContext.CompetitionEntries.Add(new CompetitionEntry
        {
            CompetitionEditionId = editionId,
            CompetitorId = competitorId,
            Seed = seed
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> CreateAndRegisterAsync(
        long editionId,
        CompetitorInput input,
        bool allowDuplicateName = false,
        CancellationToken cancellationToken = default)
    {
        Validate(input);

        if (!await dbContext.CompetitionEditions.AnyAsync(x => x.Id == editionId, cancellationToken))
        {
            throw new ValidationException("Soutěž nebyla nalezena.");
        }

        await EnsureDuplicateNameIsConfirmedAsync(input, allowDuplicateName, cancellationToken);
        var seed = await GetLowestAvailableSeedAsync(editionId, cancellationToken);

        var competitor = CreateCompetitor(input);
        dbContext.CompetitionEntries.Add(new CompetitionEntry
        {
            CompetitionEditionId = editionId,
            Competitor = competitor,
            Seed = seed
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return competitor.Id;
    }

    private async Task<int> GetLowestAvailableSeedAsync(
        long editionId,
        CancellationToken cancellationToken)
    {
        var usedSeeds = await dbContext.CompetitionEntries
            .AsNoTracking()
            .Where(x => x.CompetitionEditionId == editionId)
            .OrderBy(x => x.Seed)
            .Select(x => x.Seed)
            .ToListAsync(cancellationToken);

        var availableSeed = 1;
        foreach (var usedSeed in usedSeeds)
        {
            if (usedSeed == availableSeed)
            {
                availableSeed++;
            }
            else if (usedSeed > availableSeed)
            {
                break;
            }
        }

        return availableSeed;
    }

    public async Task<SeedUpdateResult?> UpdateSeedAsync(
        long editionId,
        long entryId,
        int seed,
        CancellationToken cancellationToken = default)
    {
        ValidateSeed(seed);

        if (!dbContext.Database.IsRelational())
        {
            return await UpdateSeedCoreAsync(editionId, entryId, seed, cancellationToken);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await UpdateSeedCoreAsync(editionId, entryId, seed, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<SeedUpdateResult?> UpdateSeedCoreAsync(
        long editionId,
        long entryId,
        int seed,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.CompetitionEntries.SingleOrDefaultAsync(
            x => x.Id == entryId && x.CompetitionEditionId == editionId,
            cancellationToken);
        if (entry is null)
        {
            return null;
        }

        if (entry.Seed == seed)
        {
            return new SeedUpdateResult(false, null);
        }

        var conflictingEntry = await dbContext.CompetitionEntries
            .Include(x => x.Competitor)
            .SingleOrDefaultAsync(
                x => x.CompetitionEditionId == editionId && x.Seed == seed,
                cancellationToken);

        if (conflictingEntry is null)
        {
            entry.Seed = seed;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SeedUpdateResult(false, null);
        }

        var originalSeed = entry.Seed;
        var temporarySeed = await dbContext.CompetitionEntries
            .Where(x => x.CompetitionEditionId == editionId)
            .MaxAsync(x => x.Seed, cancellationToken) + 1;

        conflictingEntry.Seed = temporarySeed;
        await dbContext.SaveChangesAsync(cancellationToken);
        entry.Seed = seed;
        await dbContext.SaveChangesAsync(cancellationToken);
        conflictingEntry.Seed = originalSeed;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SeedUpdateResult(
            true,
            $"{conflictingEntry.Competitor.LastName} {conflictingEntry.Competitor.FirstName}");
    }

    public async Task<bool> RemoveAsync(
        long editionId,
        long entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.CompetitionEntries.SingleOrDefaultAsync(
            x => x.Id == entryId && x.CompetitionEditionId == editionId,
            cancellationToken);
        if (entry is null)
        {
            return false;
        }

        if (await dbContext.DisciplineTeamMembers.AnyAsync(
                x => x.CompetitionEntryId == entryId,
                cancellationToken))
        {
            throw new ValidationException(
                "Soutěžícího nelze odhlásit, protože už je zařazen do týmů nebo výsledků soutěže.");
        }

        dbContext.CompetitionEntries.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureSeedIsAvailableAsync(
        long editionId,
        int seed,
        long? exceptEntryId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.CompetitionEntries.AnyAsync(
                x => x.CompetitionEditionId == editionId &&
                     x.Seed == seed &&
                     (!exceptEntryId.HasValue || x.Id != exceptEntryId.Value),
                cancellationToken))
        {
            throw new ValidationException("Toto nasazení už v soutěži používá jiný soutěžící.");
        }
    }

    private async Task EnsureDuplicateNameIsConfirmedAsync(
        CompetitorInput input,
        bool allowDuplicateName,
        CancellationToken cancellationToken)
    {
        if (allowDuplicateName)
        {
            return;
        }

        var firstName = input.FirstName.Trim().ToUpper();
        var lastName = input.LastName.Trim().ToUpper();
        if (await dbContext.Competitors.AnyAsync(
                x => x.FirstName.ToUpper() == firstName && x.LastName.ToUpper() == lastName,
                cancellationToken))
        {
            throw new DuplicateCompetitorException();
        }
    }

    private static Competitor CreateCompetitor(CompetitorInput input) => new()
    {
        FirstName = input.FirstName.Trim(),
        LastName = input.LastName.Trim(),
        DateOfBirth = input.DateOfBirth
    };

    private static void Validate(CompetitorInput input)
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), validationResults, true))
        {
            throw new ValidationException(validationResults[0].ErrorMessage);
        }
    }

    private static void ValidateSeed(int seed)
    {
        if (seed <= 0)
        {
            throw new ValidationException("Nasazení musí být kladné celé číslo.");
        }
    }
}
