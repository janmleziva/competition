namespace Competition.Configuration;

public sealed class AnonymousResultEditingSettings
{
    public const string SectionName = "AnonymousResultEditing";

    public bool Enabled { get; set; } = true;

    public int PermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;
}
