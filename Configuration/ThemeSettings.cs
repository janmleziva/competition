namespace Competition.Configuration;

public sealed class ThemeSettings
{
    public const string SectionName = "Theme";

    public string Background { get; set; } = "#0b1020";

    public string BackgroundSoft { get; set; } = "#334155";

    public string Panel { get; set; } = "rgba(255, 255, 255, 0.92)";

    public string Text { get; set; } = "#111827";

    public string Muted { get; set; } = "#5b6477";

    public string Accent { get; set; } = "#2563eb";

    public string AccentStrong { get; set; } = "#1d4ed8";

    public string FontFamily { get; set; } = "Segoe UI, sans-serif";

    public string BorderRadius { get; set; } = "24px";
}
