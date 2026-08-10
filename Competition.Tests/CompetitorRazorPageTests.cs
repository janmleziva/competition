using System.Security.Claims;
using Competition.Models;
using Competition.Pages.Editions;
using Competition.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Tests;

public sealed class CompetitorRazorPageTests
{
    [Fact]
    public void CatalogWritePages_RequireAuthorization()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(Pages.Competitors.CreateModel), typeof(AuthorizeAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(Pages.Competitors.EditModel), typeof(AuthorizeAttribute)));
    }

    [Fact]
    public async Task EditionRegistrationGet_IsAvailableAnonymously()
    {
        var page = CreateRegistrationPage(authenticated: false);

        var result = await page.OnGetAsync(7, default);

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task EditionRegistrationPost_AnonymousUserIsChallenged()
    {
        var service = new RecordingCompetitorService();
        var page = CreateRegistrationPage(authenticated: false, service);

        var result = await page.OnPostRegisterAsync(7, default);

        Assert.IsType<ChallengeResult>(result);
        Assert.Equal(0, service.RegisterCalls);
    }

    [Fact]
    public async Task EditionRegistrationPost_AuthenticatedUserCanRegister()
    {
        var service = new RecordingCompetitorService();
        var page = CreateRegistrationPage(authenticated: true, service);
        page.CompetitorId = 12;
        page.Seed = 3;

        var result = await page.OnPostRegisterAsync(7, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, service.RegisterCalls);
    }

    [Fact]
    public async Task CreateCompetitorPost_DuplicateNameShowsConfirmation()
    {
        var service = new RecordingCompetitorService { ThrowDuplicateOnCreate = true };
        var page = new Competition.Pages.Competitors.CreateModel(service)
        {
            Input = new CompetitorInput { FirstName = "Jan", LastName = "Novák" }
        };

        var result = await page.OnPostAsync(default);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowDuplicateConfirmation);
    }

    [Fact]
    public async Task CreateAndRegisterPost_AuthenticatedUserUsesCombinedOperation()
    {
        var service = new RecordingCompetitorService();
        var page = CreateRegistrationPage(authenticated: true, service);
        var input = new CompetitorInput { FirstName = "Eva", LastName = "Malá" };

        var result = await page.OnPostCreateAndRegisterAsync(7, input, false, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, service.CreateAndRegisterCalls);
    }

    [Fact]
    public async Task DeleteCompetitorPost_AnonymousUserIsChallenged()
    {
        var service = new RecordingCompetitorService();
        var page = new Competition.Pages.Competitors.IndexModel(service)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await page.OnPostDeleteAsync(12, default);

        Assert.IsType<ChallengeResult>(result);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task UpdateSeedPost_SwapExplainsWhichCompetitorWasAffected()
    {
        var service = new RecordingCompetitorService
        {
            SeedUpdateResult = new SeedUpdateResult(true, "Novák Jan")
        };
        var page = CreateRegistrationPage(authenticated: true, service);

        var result = await page.OnPostUpdateSeedAsync(7, 12, 1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Nasazení bylo prohozeno se soutěžícím Novák Jan.", page.StatusMessage);
    }

    private static CompetitorsModel CreateRegistrationPage(
        bool authenticated,
        RecordingCompetitorService? service = null)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "test")
            : new ClaimsIdentity();
        return new CompetitorsModel(service ?? new RecordingCompetitorService())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class RecordingCompetitorService : ICompetitorAdministrationService
    {
        public int RegisterCalls { get; private set; }
        public int CreateAndRegisterCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool ThrowDuplicateOnCreate { get; init; }
        public SeedUpdateResult SeedUpdateResult { get; init; } = new(false, null);

        public Task<IReadOnlyList<CompetitorSummary>> SearchAsync(string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CompetitorSummary>>([]);

        public Task<CompetitorDetails?> GetAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CompetitorDetails?>(null);

        public Task<long> CreateAsync(CompetitorInput input, bool allowDuplicateName = false, CancellationToken cancellationToken = default)
        {
            if (ThrowDuplicateOnCreate && !allowDuplicateName)
            {
                throw new DuplicateCompetitorException();
            }

            return Task.FromResult(1L);
        }

        public Task<bool> UpdateAsync(long id, CompetitorInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(true);
        }

        public Task<EditionRegistrationDetails?> GetEditionRegistrationAsync(long editionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EditionRegistrationDetails?>(new EditionRegistrationDetails(editionId, "Cup", [], []));

        public Task RegisterAsync(long editionId, long competitorId, int seed, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return Task.CompletedTask;
        }

        public Task<long> CreateAndRegisterAsync(long editionId, CompetitorInput input, bool allowDuplicateName = false, CancellationToken cancellationToken = default)
        {
            CreateAndRegisterCalls++;
            return Task.FromResult(1L);
        }

        public Task<SeedUpdateResult?> UpdateSeedAsync(long editionId, long entryId, int seed, CancellationToken cancellationToken = default) =>
            Task.FromResult<SeedUpdateResult?>(SeedUpdateResult);

        public Task<bool> RemoveAsync(long editionId, long entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
