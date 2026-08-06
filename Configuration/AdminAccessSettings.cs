namespace Competition.Configuration;

public sealed class AdminAccessSettings
{
    public const string SectionName = "AdminAccess";

    public string Username { get; set; } = "honza";

    public string Password { get; set; } = "cruchot";

    public double CookieLifetimeHours { get; set; } = 8;

    public string[] ProtectedPathPrefixes { get; set; } = ["/Admin"];
}
