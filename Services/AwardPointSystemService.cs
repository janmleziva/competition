using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class AwardPointSystemService(CompetitionDbContext dbContext) : IAwardPointSystemService
{
    public async Task<IReadOnlyList<AwardPointSystemItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AwardPointSystems.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new AwardPointSystemItem(
                x.Id,
                x.Name,
                x.Rules.OrderBy(rule => rule.Rank)
                    .Select(rule => new AwardPointRuleItem(rule.Rank, rule.Points))
                    .ToList(),
                x.CompetitionDisciplines.Any()))
            .ToListAsync(cancellationToken);

    public async Task<long> CreateAsync(AwardPointSystemInput input, CancellationToken cancellationToken = default)
    {
        var rules = Validate(input);
        var name = input.Name.Trim();
        if (await dbContext.AwardPointSystems.AnyAsync(x => x.Name == name, cancellationToken))
        {
            throw new ValidationException("Bodovací systém s tímto názvem už existuje.");
        }

        return await CreateValidatedAsync(name, rules, cancellationToken);
    }

    public async Task<long?> CreateCopyAsync(
        long sourceId, AwardPointSystemInput input, CancellationToken cancellationToken = default)
    {
        var rules = Validate(input);
        var sourceName = await dbContext.AwardPointSystems.AsNoTracking()
            .Where(x => x.Id == sourceId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (sourceName is null)
        {
            return null;
        }

        var requestedName = input.Name.Trim();
        var name = requestedName == sourceName
            ? await GetCopyNameAsync(requestedName, cancellationToken)
            : requestedName;
        if (await dbContext.AwardPointSystems.AnyAsync(x => x.Name == name, cancellationToken))
        {
            throw new ValidationException("Bodovací systém s tímto názvem už existuje.");
        }

        return await CreateValidatedAsync(name, rules, cancellationToken);
    }

    public async Task<bool> UpdateAsync(long id, AwardPointSystemInput input, CancellationToken cancellationToken = default)
    {
        var rules = Validate(input);
        var system = await dbContext.AwardPointSystems.Include(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (system is null)
        {
            return false;
        }
        if (await dbContext.CompetitionDisciplines.AnyAsync(x => x.AwardPointSystemId == id, cancellationToken))
        {
            throw new ValidationException("Používaný bodovací systém nelze upravit. Pro konkrétní disciplínu vytvořte jeho kopii.");
        }

        var name = input.Name.Trim();
        if (await dbContext.AwardPointSystems.AnyAsync(x => x.Id != id && x.Name == name, cancellationToken))
        {
            throw new ValidationException("Bodovací systém s tímto názvem už existuje.");
        }

        system.Name = name;
        dbContext.RankingPointRules.RemoveRange(system.Rules);
        foreach (var rule in rules)
        {
            system.Rules.Add(new RankingPointRule { Rank = rule.Rank, Points = rule.Points });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<long> CreateValidatedAsync(
        string name, IReadOnlyList<RankingPointRuleInput> rules, CancellationToken cancellationToken)
    {
        var system = new AwardPointSystem { Name = name };
        foreach (var rule in rules)
        {
            system.Rules.Add(new RankingPointRule { Rank = rule.Rank, Points = rule.Points });
        }

        dbContext.AwardPointSystems.Add(system);
        await dbContext.SaveChangesAsync(cancellationToken);
        return system.Id;
    }

    private async Task<string> GetCopyNameAsync(string sourceName, CancellationToken cancellationToken)
    {
        for (var copyNumber = 1; ; copyNumber++)
        {
            var suffix = copyNumber == 1 ? " – kopie" : $" – kopie {copyNumber}";
            var root = sourceName[..Math.Min(sourceName.Length, 120 - suffix.Length)].TrimEnd();
            var candidate = root + suffix;
            if (!await dbContext.AwardPointSystems.AnyAsync(x => x.Name == candidate, cancellationToken))
            {
                return candidate;
            }
        }
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var system = await dbContext.AwardPointSystems.Include(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (system is null)
        {
            return false;
        }
        if (await dbContext.CompetitionDisciplines.AnyAsync(x => x.AwardPointSystemId == id, cancellationToken))
        {
            throw new ValidationException("Používaný bodovací systém nelze smazat.");
        }

        dbContext.RankingPointRules.RemoveRange(system.Rules);
        dbContext.AwardPointSystems.Remove(system);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IReadOnlyList<RankingPointRuleInput> Validate(AwardPointSystemInput input)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
        {
            throw new ValidationException(results[0].ErrorMessage);
        }
        if (input.Rules.Count == 0)
        {
            throw new ValidationException("Zadejte body alespoň pro jedno konečné pořadí.");
        }
        foreach (var rule in input.Rules)
        {
            results.Clear();
            if (!Validator.TryValidateObject(rule, new ValidationContext(rule), results, true))
            {
                throw new ValidationException(results[0].ErrorMessage);
            }
        }
        if (input.Rules.Select(x => x.Rank).Distinct().Count() != input.Rules.Count)
        {
            throw new ValidationException("Každé konečné pořadí smí být v systému pouze jednou.");
        }

        var ordered = input.Rules.OrderBy(x => x.Rank).ToList();
        if (!ordered.Select(x => x.Rank).SequenceEqual(Enumerable.Range(1, ordered.Count)))
        {
            throw new ValidationException("Pořadí musí tvořit souvislou řadu od 1 bez mezer.");
        }
        return ordered;
    }
}
