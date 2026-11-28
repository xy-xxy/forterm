namespace Forterm.Studio;

/// <summary>Studio keeps its own data directory so it never reads or rewrites the
/// configuration of the frozen alpha builds. The directory also names the per-user
/// IPC pipe, so Studio and an older build running side by side never steal each
/// other's controller launches.</summary>
public static class StudioPaths
{
    public const string Version = "0.4.0-alpha.16";
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("FORTERM_STUDIO_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forterm", "Studio");
}
