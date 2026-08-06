namespace Competition.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int CommandTimeoutSeconds { get; set; } = 10;
    public int MaxRetryCount { get; set; } = 1;
    public int MaxRetryDelaySeconds { get; set; } = 1;
}
