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
        if (!PlatformOwnership.IsThePlatform(root)) return; // an app packs nothing
        foreach (var (project, id) in Expected)
        {
            var text = File.ReadAllText(Path.Combine(root, project));
            Assert.Matches(@"<IsPackable>\s*true\s*</IsPackable>", text);
            Assert.Contains($"<PackageId>{id}</PackageId>", text, StringComparison.Ordinal);
            Assert.Matches(@"<Description>[^<]{20,}</Description>", text);
        }
        // Api references WebAssembly.Server (single-origin hosting), which injects its WASM debug proxy as content: kept out
        // of the package, or pack fails (NU5100). Removed once by mistake when Api briefly did not need it (Pkg 3.2 → 3.3).
        Assert.Contains("<IncludeContentInPack>false</IncludeContentInPack>", File.ReadAllText(Path.Combine(root, "src/Api/Perezosoft.Api.csproj")), StringComparison.Ordinal);
    }

    [Fact]
    public void APackableProject_ReferencesOnlyPackableProjects()
    {
        var root = RepoRoot();
        if (!PlatformOwnership.IsThePlatform(root)) return;
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
        if (!PlatformOwnership.IsThePlatform(root)) return;
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
        // The platform's controllers come in as an application part by name, not by MVC's discovery (PlatformHosting, Pkg 3.3).
        Assert.Contains(".AddApplicationPart(", File.ReadAllText(Path.Combine(api, "PlatformHosting.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramCs_IsTheComposition_Only() // Pkg 3.3
    {
        // Program.cs is the same in the platform and every app (the starter ships it): it composes, in this order, the
        // platform's services (AddPlatform), the app's (AddAppServices — after, so an app can replace a platform service),
        // the platform's pipeline and endpoints (MapPlatformAsync), the app's endpoints, the route-table guard, Run.
        // Any registration or middleware of its own would be a fork of the platform in every app.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Host", "Program.cs")).Replace("\r\n", "\n");
        var code = program.Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("using ", StringComparison.Ordinal))
            .ToList();
        Assert.True(code.Count <= 10, $"Program.cs carries {code.Count} lines of code; the composition is ~8:\n{string.Join('\n', code)}");
        int At(string call) { var i = program.IndexOf(call, StringComparison.Ordinal); Assert.True(i >= 0, $"Program.cs does not call {call}"); return i; }
        var order = new[] { "LocalDotEnv.Load()", "builder.AddPlatform<AppDbContext>(AppComposition.ConfigurePlatform)", "builder.Services.AddAppServices(builder.Configuration)",
                            "builder.Build()", "await app.MapPlatformAsync()", "app.MapAppEndpoints()", "RouteTableGuard.EnsureUnique(", "app.Run()" };
        var positions = order.Select(At).ToList();
        Assert.True(positions.SequenceEqual(positions.Order()), "Program.cs composes out of order: " + string.Join(" → ", order));
    }

    [Fact]
    public void AddPlatform_TakesTheHostsOptions_AsAnOptionalCallback() // Pkg 3.7
    {
        // The extension point every seam hangs off (ADR-034 §4): builder.AddPlatform<TContext>(o => ...). Optional, so the
        // starter's Program.cs line stays the same until an app has something to set; a required parameter would break every app.
        var method = typeof(PlatformHosting).GetMethod(nameof(PlatformHosting.AddPlatform))!;
        var configure = method.GetParameters().Single(p => p.Name == "configure");
        Assert.Equal(typeof(Action<Perezosoft.Core.PlatformOptions>), configure.ParameterType);
        Assert.True(configure.IsOptional);
    }

    [Fact]
    public void PackagingSettings_LiveOnceInThePackingFiles() // build/Packing.props/.targets, out of Directory.Build.* since Pkg 5.2
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "build", "Packing.props"));
        var targets = File.ReadAllText(Path.Combine(RepoRoot(), "build", "Packing.targets"));
        foreach (var root in new[] { "Directory.Build.props", "Directory.Build.targets" })
            Assert.Contains("build/Packing.", File.ReadAllText(Path.Combine(RepoRoot(), root)), StringComparison.Ordinal);
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
