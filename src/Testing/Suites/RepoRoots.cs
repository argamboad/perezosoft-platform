namespace Perezosoft.Testing.Suites;

/// <summary>Where the repo under test is, for every suite (Pkg 5.1b): the platform's and every app's root holds both files.</summary>
public static class RepoRoots
{
    /// <summary>
    /// The folder above the test binaries that holds <c>platform-stamp.json</c> AND <c>Directory.Build.props</c>: the build
    /// copies the stamp beside a host's binaries (and so into a test project's bin/), never the props.
    /// </summary>
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(File.Exists(Path.Combine(dir.FullName, "platform-stamp.json"))
                                    && File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (platform-stamp.json beside Directory.Build.props) from the test assembly.");
    }
}
