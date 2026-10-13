namespace Perezosoft.Platform.DryRun;

/// <summary>The one type in the release pipeline's probe package (Pkg 2.3): restoring it proves the feed end to end.</summary>
public static class DryRun
{
    /// <summary>The package's own version, as MinVer stamped it.</summary>
    public static string Version =>
        typeof(DryRun).Assembly.GetName().Version?.ToString() ?? "unknown";
}
