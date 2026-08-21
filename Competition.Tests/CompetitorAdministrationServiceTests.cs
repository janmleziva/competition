using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Domain;
using Competition.Models;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class CompetitorAdministrationServiceTests
{
    [Fact]
    public async Task Catalog_CreateEditAndSearch_ReusesCompetitorRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new CompetitorAdministrationService(dbContext);

        var competitorId = await service.CreateAsync(Input(" Jan ", " Novák "));
        Assert.True(await service.UpdateAsync(competitorId, Input("Jan", "Novotný")));

        var result = await service.SearchAsync("Novot");

        var competitor = Assert.Single(result);
        Assert.Equal("Jan", competitor.FirstName);
        Assert.Equal("Novotný", competitor.LastName);
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateCompetitor()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, firstCompetitorId, _) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, firstCompetitorId, 1);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.RegisterAsync(editionId, firstCompetitorId, 2));

        Assert.Contains("už je", error.Message);
        Assert.Equal(1, await dbContext.CompetitionEntries.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameRequiresExplicitConfirmation()
    {
        await using var dbContext = CreateDbContext();
        var service = new CompetitorAdministrationService(dbContext);
        await service.CreateAsync(Input("Jan", "Novák"));

        await Assert.ThrowsAsync<DuplicateCompetitorException>(
            () => service.CreateAsync(Input(" jan ", " novák ")));

        await service.CreateAsync(Input(" jan ", " novák "), allowDuplicateName: true);
        Assert.Equal(2, await dbContext.Competitors.CountAsync());
    }

    [Fact]
    public async Task CreateAndRegisterAsync_UsesLowestAvailableSeed()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, firstCompetitorId, secondCompetitorId) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, firstCompetitorId, 1);
        await service.RegisterAsync(editionId, secondCompetitorId, 3);

        var competitorId = await service.CreateAndRegisterAsync(
            editionId,
            Input("  Eva  ", "  Malá  "));

        var competitor = await dbContext.Competitors.FindAsync(competitorId);
        var entry = await dbContext.CompetitionEntries.SingleAsync(x => x.CompetitorId == competitorId);
        Assert.Equal("Eva", competitor!.FirstName);
        Assert.Equal("Malá", competitor.LastName);
        Assert.Equal(competitorId, entry.CompetitorId);
        Assert.Equal(editionId, entry.CompetitionEditionId);
        Assert.Equal(2, entry.Seed);
    }

    [Fact]
    public async Task UpdateSeedAsync_UsedSeedSwapsBothCompetitors()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, firstCompetitorId, secondCompetitorId) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, firstCompetitorId, 1);
        await service.RegisterAsync(editionId, secondCompetitorId, 2);
        var secondEntryId = await dbContext.CompetitionEntries
            .Where(x => x.CompetitorId == secondCompetitorId)
            .Select(x => x.Id)
            .SingleAsync();

        var result = await service.UpdateSeedAsync(editionId, secondEntryId, 1);

        Assert.NotNull(result);
        Assert.True(result.WasSwapped);
        Assert.Equal("Novák Jan", result.SwappedCompetitorName);
        Assert.Equal(1, (await dbContext.CompetitionEntries.FindAsync(secondEntryId))!.Seed);
        Assert.Equal(
            2,
            await dbContext.CompetitionEntries
                .Where(x => x.CompetitorId == firstCompetitorId)
                .Select(x => x.Seed)
                .SingleAsync());
    }

    [Fact]
    public async Task RemoveAsync_WithTeamMembership_ReturnsClearValidationError()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, firstCompetitorId, _) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, firstCompetitorId, 1);
        var entry = await dbContext.CompetitionEntries.SingleAsync();

        var discipline = new Discipline { Name = "Tenis" };
        var configuredDiscipline = new CompetitionDiscipline
        {
            CompetitionEditionId = editionId,
            Discipline = discipline,
            Order = 1,
            TeamSize = 1
        };
        var team = new DisciplineTeam { CompetitionDiscipline = configuredDiscipline, Seed = 1 };
        dbContext.Add(team);
        await dbContext.SaveChangesAsync();
        dbContext.DisciplineTeamMembers.Add(new DisciplineTeamMember
        {
            CompetitionDisciplineId = configuredDiscipline.Id,
            DisciplineTeam = team,
            CompetitionEntryId = entry.Id,
            Order = 1
        });
        await dbContext.SaveChangesAsync();

        var registration = await service.GetEditionRegistrationAsync(editionId);
        Assert.False(Assert.Single(registration!.Entries).CanRemove);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.RemoveAsync(editionId, entry.Id));

        Assert.Contains("nelze odhlásit", error.Message);
        Assert.NotNull(await dbContext.CompetitionEntries.FindAsync(entry.Id));
    }

    [Fact]
    public async Task RemoveAsync_WithoutDependencies_RemovesOnlyEditionRegistration()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, firstCompetitorId, _) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, firstCompetitorId, 1);
        var entryId = await dbContext.CompetitionEntries.Select(x => x.Id).SingleAsync();

        var registration = await service.GetEditionRegistrationAsync(editionId);
        Assert.True(Assert.Single(registration!.Entries).CanRemove);

        Assert.True(await service.RemoveAsync(editionId, entryId));

        Assert.Empty(await dbContext.CompetitionEntries.ToListAsync());
        Assert.NotNull(await dbContext.Competitors.FindAsync(firstCompetitorId));
    }

    [Fact]
    public async Task DeleteAsync_UnregisteredCompetitorDeletesCatalogRecord()
    {
        await using var dbContext = CreateDbContext();
        var service = new CompetitorAdministrationService(dbContext);
        var competitorId = await service.CreateAsync(Input("Eva", "Malá"));

        Assert.True(await service.DeleteAsync(competitorId));

        Assert.Null(await dbContext.Competitors.FindAsync(competitorId));
    }

    [Fact]
    public async Task DeleteAsync_RegisteredCompetitorIsRejected()
    {
        await using var dbContext = CreateDbContext();
        var (editionId, competitorId, _) = await SeedCatalogAsync(dbContext);
        var service = new CompetitorAdministrationService(dbContext);
        await service.RegisterAsync(editionId, competitorId, 1);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.DeleteAsync(competitorId));

        Assert.Contains("nemůže být smazán", error.Message);
        Assert.NotNull(await dbContext.Competitors.FindAsync(competitorId));
    }

    private static async Task<(long EditionId, long FirstCompetitorId, long SecondCompetitorId)> SeedCatalogAsync(
        CompetitionDbContext dbContext)
    {
        var edition = new CompetitionEdition
        {
            Name = "Summer Cup",
            City = "Prague",
            StartDate = new DateOnly(2026, 8, 22),
            EndDate = new DateOnly(2026, 8, 23),
            CreationToken = Guid.NewGuid()
        };
        var first = new Competitor { FirstName = "Jan", LastName = "Novák" };
        var second = new Competitor { FirstName = "Petr", LastName = "Svoboda" };
        dbContext.AddRange(edition, first, second);
        await dbContext.SaveChangesAsync();
        return (edition.Id, first.Id, second.Id);
    }

    private static CompetitionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CompetitionDbContext(options);
    }

    private static CompetitorInput Input(string firstName, string lastName) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateOnly(1990, 1, 2)
    };
}
