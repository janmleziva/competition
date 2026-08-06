using Competition.Configuration;
using Microsoft.Extensions.Options;

namespace Competition.Services;

public sealed class AdminCredentialValidator(IOptionsMonitor<AdminAccessSettings> settings)
{
    public bool IsValid(string username, string password)
    {
        var current = settings.CurrentValue;

        return string.Equals(username, current.Username, StringComparison.Ordinal) &&
               string.Equals(password, current.Password, StringComparison.Ordinal);
    }
}
