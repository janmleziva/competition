using Competition.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Competition.Services;

public sealed class ConfigurableAdminAuthorizationMiddleware(
    RequestDelegate next,
    IOptionsMonitor<AdminAccessSettings> settings)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresAdminAccess(context.Request.Path) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync();
            return;
        }

        await next(context);
    }

    private bool RequiresAdminAccess(PathString requestPath)
    {
        return settings.CurrentValue.ProtectedPathPrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            requestPath.StartsWithSegments(NormalizePrefix(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static PathString NormalizePrefix(string prefix) =>
        prefix.StartsWith('/') ? prefix : $"/{prefix}";
}
