using System.Text.RegularExpressions;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 3.1 (ADR-034): the shape of what the platform publishes. <c>Perezosoft.Platform.Core</c>, <c>.Infrastructure</c> and
/// <c>.Api</c> are packable, named by id, and each package only depends on packages the platform also publishes — a package
/// that referenced an unpublished project would restore nowhere. The packaging settings live once, in
/// <c>Directory.Build.props</c>: versions come from MinVer only when packing (a Docker build has no .git, and an ordinary
/// build needs no version), the .pdb rides inside the .nupkg (GitHub Packages serves no symbol package) and SourceLink maps
/// the sources back to GitHub. The proof that the packages restore is the release dry run and a scratch app; these hold the
/// contract that proof depends on. Producer-only: an app packs nothing.
/// </summary>
public class PackagingContractTests
{
    private static readonly (string Project, string Id)[] Expected =
    [
        ("src/Core/Perezosoft.Core.csproj", "Perezosoft.Platform.Core"),
        ("src/Infrastructure/Perezosoft.Infrastructure.csproj", "Perezosoft.Platform.Infrastructure"),
        ("src/Api/Perezosoft.Api.csproj", "Perezosoft.Platform.Api"),
    ];

    [Fact]
    public void CoreInfrastructureAndApi_ArePackable_UnderTheirPlatformIds_WithADescription()
    {
        var root = RepoRoot();
        if (!Architecture.PlatformOwnership.IsThePlatform(root)) return; // an app packs nothing
        foreach (var (project, id) in Expected)
        {
            var text = File.ReadAllText(Path.Combine(root, project));
            Assert.Matches(@"<IsPackable>\s*true\s*</IsPackable>", text);
            Assert.Contains($"<PackageId>{id}</PackageId>", text, StringComparison.Ordinal);
            Assert.Matches(@"<Description>[^<]{20,}</Description>", text);
        }
    }

    [Fact]
    public void APackableProject_ReferencesOnlyPackableProjects()
    {
        var root = RepoRoot();
        if (!Architecture.PlatformOwnership.IsThePlatform(root)) return;
        var packable = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(p => !p.Rel.Contains("/obj/", StringComparison.Ordinal) && !p.Rel.StartsWith(".claude/", StringComparison.Ordinal))
            .Select(p => (p.Full, p.Rel, Text: File.ReadAllText(p.Full)))
            .Where(p => Regex.IsMatch(p.Text, @"<IsPackable>\s*true\s*</IsPackable>"))
            .ToList();
        Assert.True(packable.Count >= 4, "probe: the DryRun sample and the three platform projects are packable"); // 1 sample + 3
        var names = packable.Select(p => Path.GetFileName(p.Full)).ToHashSet(StringComparer.Ordinal);
        foreach (var (full, rel, text) in packable)
            foreach (Match reference in Regex.Matches(text, @"<ProjectReference\s+Include=""([^""]+)"""))
            {
                var target = Path.GetFileName(reference.Groups[1].Value.Replace('\\', '/'));
                Assert.True(names.Contains(target), $"{rel} is packable but references {target}, which is not: the package would depend on something never published");
            }
    }

    [Fact]
    public void TheApiPackage_IsALibrary_AndTheHostCarriesTheApp() // Pkg 3.2
    {
        // The package must hold the platform and nothing an app owns: no Program, no app composition, no slice (the Notes
        // sample would otherwise ship to every app), no host configuration. All of that is the host's (src/Host here).
        var root = RepoRoot();
        if (!Architecture.PlatformOwnership.IsThePlatform(root)) return;
        var api = Path.Combine(root, "src", "Api");
        var host = Path.Combine(root, "src", "Host");
        Assert.StartsWith("<Project Sdk=\"Microsoft.NET.Sdk\">", File.ReadAllText(Path.Combine(api, "Perezosoft.Api.csproj")).TrimStart('﻿'));
        foreach (var hostOnly in new[] { "Program.cs", "AppComposition.cs", "AppDbContextFactory.cs", "appsettings.json", "Features", Path.Combine("Properties", "launchSettings.json") })
        {
            Assert.False(File.Exists(Path.Combine(api, hostOnly)) || Directory.Exists(Path.Combine(api, hostOnly)), $"src/Api/{hostOnly} belongs to the host, not the package");
            Assert.True(File.Exists(Path.Combine(host, hostOnly)) || Directory.Exists(Path.Combine(host, hostOnly)), $"src/Host/{hostOnly} is missing");
        }
        Assert.True(Directory.Exists(Path.Combine(host, "Features", "Notes")), "probe: the sample slice lives in the host");
        var hostProject = File.ReadAllText(Path.Combine(host, "Perezosoft.Host.csproj"));
        Assert.StartsWith("<Project Sdk=\"Microsoft.NET.Sdk.Web\">", hostProject.TrimStart('﻿'));
        Assert.DoesNotMatch(@"<IsPackable>\s*true", hostProject); // the host is never packed
        // The platform's controllers come in as an application part by name, not by MVC's discovery.
        Assert.Contains(".AddApplicationPart(", File.ReadAllText(Path.Combine(host, "Program.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingSettings_LiveOnceInDirectoryBuildProps()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var targets = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.targets"));
        // MinVer runs only when packing: a Docker build has no .git (MinVer would warn, and warnings are errors).
        Assert.Contains("<MinVerSkip Condition=\"'$(_IsPacking)' != 'true' and '$(MinVerSkip)' == ''\">true</MinVerSkip>", props, StringComparison.Ordinal);
        // GitHub Packages serves no .snupkg: the symbols travel inside the .nupkg, and SourceLink maps them to GitHub. In the
        // .targets, where the project's own IsPackable is known; for a packable project only.
        Assert.Contains("<PropertyGroup Condition=\"'$(IsPackable)' == 'true'\">", targets, StringComparison.Ordinal);
        Assert.Contains("$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb", targets, StringComparison.Ordinal);
        Assert.DoesNotContain("<DebugType>embedded</DebugType>", targets, StringComparison.Ordinal); // NU5026: pack looks for the file
        Assert.Contains("<PublishRepositoryUrl>true</PublishRepositoryUrl>", targets, StringComparison.Ordinal);
        Assert.Contains("<EmbedUntrackedSources>true</EmbedUntrackedSources>", targets, StringComparison.Ordinal);
        Assert.Contains("<ContinuousIntegrationBuild", targets, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeSymbols", props + targets, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
