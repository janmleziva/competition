using Competition.Models;
using Competition.Pages.Editions;
using Competition.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Tests;

public sealed class EditionRazorPageTests
{
    [Fact]
    public async Task CreatePost_WithValidationErrors_DoesNotCallService()
    {
        var service = new RecordingEditionService();
        var page = new CreateModel(service)
        {
            CreationToken = Guid.NewGuid(),
            Input = ValidInput()
        };
        page.ModelState.AddModelError("Input.EndDate", "End date must be on or after the start date.");

        var result = await page.OnPostAsync(default);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task EditPost_WithValidationErrors_DoesNotCallService()
    {
        var service = new RecordingEditionService();
        var page = new EditModel(service) { Input = ValidInput() };
        page.ModelState.AddModelError("Input.Name", "The Competition name field is required.");

        var result = await page.OnPostAsync(42, default);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, service.UpdateCalls);
        Assert.Equal(42, page.EditionId);
    }

    [Fact]
    public async Task DetailsGet_ForMissingEdition_ReturnsNotFound()
    {
        var page = new DetailsModel(new RecordingEditionService());

        var result = await page.OnGetAsync(404, default);

        Assert.IsType<NotFoundResult>(result);
    }

    private static EditionInput ValidInput() => new()
    {
        Name = "Summer Cup",
        City = "Prague",
        StartDate = new DateOnly(2026, 8, 22),
        EndDate = new DateOnly(2026, 8, 23)
    };

    private sealed class RecordingEditionService : IEditionAdministrationService
    {
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<IReadOnlyList<EditionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EditionSummary>>([]);

        public Task<EditionDetails?> GetAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<EditionDetails?>(null);

        public Task<long> CreateAsync(EditionInput input, Guid creationToken, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(1L);
        }

        public Task<bool> UpdateAsync(long id, EditionInput input, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> SetActiveAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
