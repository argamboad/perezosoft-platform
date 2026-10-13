namespace Perezosoft.Platform.DryRun;

/// <summary>The one type in the release pipeline's probe package (Pkg 2.3): restoring it proves the feed end to end.</summary>
public static class DryRun
{
    /// <summary>The package's own version, as MinVer stamped it (the informational version: 0.0.1-dry.1+&lt;commit&gt;).</summary>
    public static string Version =>
        typeof(DryRun).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute v, ..] ? v.InformationalVersion : "unknown";
}
