using System.ComponentModel.DataAnnotations;
using Competition.Data;
using Competition.Models;
using Competition.Services;
using Microsoft.EntityFrameworkCore;

namespace Competition.Tests;

public sealed class EditionAdministrationServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesFirstEditionAsActiveAndTrimsText()
    {
        await using var dbContext = CreateDbContext();
        var service = new EditionAdministrationService(dbContext);

        var id = await service.CreateAsync(ValidInput("  Summer Cup  ", "  Prague  "), Guid.NewGuid());

        var edition = await dbContext.CompetitionEditions.SingleAsync();
        Assert.Equal(id, edition.Id);
        Assert.Equal("Summer Cup", edition.Name);
        Assert.Equal("Prague", edition.City);
        Assert.True(edition.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WithSameToken_ReturnsExistingEdition()
    {
        await using var dbContext = CreateDbContext();
        var service = new EditionAdministrationService(dbContext);
        var token = Guid.NewGuid();

        var firstId = await service.CreateAsync(ValidInput("First"), token);
        var repeatedId = await service.CreateAsync(ValidInput("Ignored retry"), token);

        Assert.Equal(firstId, repeatedId);
        Assert.Equal(1, await dbContext.CompetitionEditions.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ChangesExistingEdition()
    {
        await using var dbContext = CreateDbContext();
        var service = new EditionAdministrationService(dbContext);
        var id = await service.CreateAsync(ValidInput("Before"), Guid.NewGuid());

        var updated = await service.UpdateAsync(id, ValidInput("After", "Brno"));

        Assert.True(updated);
        var details = await service.GetAsync(id);
        Assert.NotNull(details);
        Assert.Equal("After", details.Name);
        Assert.Equal("Brno", details.City);
    }

    [Fact]
    public async Task SetActiveAsync_MovesActiveSelection()
    {
        await using var dbContext = CreateDbContext();
        var service = new EditionAdministrationService(dbContext);
        var firstId = await service.CreateAsync(ValidInput("First"), Guid.NewGuid());
        var secondId = await service.CreateAsync(ValidInput("Second"), Guid.NewGuid());

        Assert.True(await service.SetActiveAsync(secondId));

        var editions = await service.ListAsync();
        Assert.False(editions.Single(x => x.Id == firstId).IsActive);
        Assert.True(editions.Single(x => x.Id == secondId).IsActive);
    }

    [Fact]
    public async Task CreateAsync_RejectsEndDateBeforeStartDate()
    {
        await using var dbContext = CreateDbContext();
        var service = new EditionAdministrationService(dbContext);
        var input = ValidInput("Invalid");
        input.EndDate = input.StartDate!.Value.AddDays(-1);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(input, Guid.NewGuid()));

        Assert.Contains("Datum konce", error.Message);
    }

    private static CompetitionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CompetitionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CompetitionDbContext(options);
    }

    private static EditionInput ValidInput(string name, string city = "Prague") => new()
    {
        Name = name,
        City = city,
        StartDate = new DateOnly(2026, 8, 22),
        EndDate = new DateOnly(2026, 8, 23)
    };
}
