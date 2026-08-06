using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Microsoft.EntityFrameworkCore;

namespace Competition.Services;

public sealed class EditionAdministrationService(CompetitionDbContext dbContext)
    : IEditionAdministrationService
{
    public async Task<IReadOnlyList<EditionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.CompetitionEditions
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .ThenBy(x => x.Name)
            .Select(x => new EditionSummary(
                x.Id,
                x.Name,
                x.City,
                x.StartDate,
                x.EndDate,
                x.IsActive))
            .ToListAsync(cancellationToken);

    public Task<EditionDetails?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        dbContext.CompetitionEditions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new EditionDetails(
                x.Id,
                x.Name,
                x.City,
                x.StartDate,
                x.EndDate,
                x.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<long> CreateAsync(
        EditionInput input,
        Guid creationToken,
        CancellationToken cancellationToken = default)
    {
        Validate(input);

        if (creationToken == Guid.Empty)
        {
            throw new ValidationException("A valid creation token is required.");
        }

        var existingId = await dbContext.CompetitionEditions
            .Where(x => x.CreationToken == creationToken)
            .Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingId is not null)
        {
            return existingId.Value;
        }

        var edition = new CompetitionEdition
        {
            Name = input.Name.Trim(),
            City = input.City.Trim(),
            StartDate = input.StartDate!.Value,
            EndDate = input.EndDate!.Value,
            CreationToken = creationToken,
            IsActive = !await dbContext.CompetitionEditions.AnyAsync(cancellationToken)
        };

        dbContext.CompetitionEditions.Add(edition);
        await dbContext.SaveChangesAsync(cancellationToken);
        return edition.Id;
    }

    public async Task<bool> UpdateAsync(
        long id,
        EditionInput input,
        CancellationToken cancellationToken = default)
    {
        Validate(input);

        var edition = await dbContext.CompetitionEditions.FindAsync([id], cancellationToken);
        if (edition is null)
        {
            return false;
        }

        edition.Name = input.Name.Trim();
        edition.City = input.City.Trim();
        edition.StartDate = input.StartDate!.Value;
        edition.EndDate = input.EndDate!.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetActiveAsync(long id, CancellationToken cancellationToken = default)
    {
        var target = await dbContext.CompetitionEditions.FindAsync([id], cancellationToken);
        if (target is null)
        {
            return false;
        }

        var current = await dbContext.CompetitionEditions.SingleOrDefaultAsync(
            x => x.IsActive && x.Id != id,
            cancellationToken);

        if (current is not null)
        {
            current.IsActive = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!target.IsActive)
        {
            target.IsActive = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static void Validate(EditionInput input)
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), validationResults, true))
        {
            throw new ValidationException(validationResults[0].ErrorMessage);
        }
    }
}
