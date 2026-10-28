using System.Text.Json;
using System.Text.RegularExpressions;
using static Perezosoft.Testing.Architecture.PlatformOwnership;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 6.1a (#455), 6.1b (#456), 6.1c (#457), 6.2 (#421), 6.3 (#422): starter/ is the app `dotnet new perezosoft-app` writes (Perezosoft.Platform.Templates), checked in
/// here and written with its own placeholder brand (AppBrand), which the template renames. What it shares with the
/// platform must not drift from it: each straight copy is the platform's file, renamed by one rule (<see cref="ToStarter"/>);
/// its package pins are the platform's; no platform brand is left for the template to miss; and nothing in it is a
/// condition the template engine would evaluate.
/// </summary>
public class StarterTests
{
    // The platform's files starter/ carries as they are, renamed. The register is among them (an app on the packages is
    // held to it, PlatformRegisterTests); the rest are the local stack.
    private static readonly string[] StraightCopies =
    [
        ".gitignore", ".gitattributes", "global.json", "nuget.config", ".config/dotnet-tools.json", "platform-ownership.json",
        "build/PlatformSource.props", "build/PlatformSource.targets", "local-ports.props", "docker/db/provision-rls-runtime-role.sql",
    ];

    // The app's trees, carried whole: every file of each is the platform's, renamed, except its project file (an app's
    // references the packages), its lockfile, the host's migrations (the platform's history is its own; an app's starts
    // from the three the starter ships) and the Postman README (the platform's describes its own sync workflow and gates).
    // The host (Pkg 6.2, #421: the platform's reference host IS the starter's), the seeded UI (6.1b, #456), the native app,
    // the journeys and the Android smoke spec (6.1c, #457), the Postman collection the seeded Home names (6.3, #422).
    private static readonly string[] CopiedTrees =
        ["src/Host", "src/Shared.Ui", "src/Web", "tests/Ui.Tests", "src/Maui", "tests/E2E.Tests", "tests/native-smoke-android", "docs/postman"];

    // What the template copies as it is, beside the register: the binaries and the vendored Bootstrap (nothing to rename,
    // and the engine would scan them for its own markers). The gate compares the binaries byte for byte.
    private static readonly string[] CopiedAsIs = ["**/*.png", "**/*.ico", "**/*.ttf", "src/Web/wwwroot/lib/**", "src/Maui/wwwroot/lib/**"];

    private static bool IsBinary(string rel) =>
        rel.EndsWith(".png", StringComparison.Ordinal) || rel.EndsWith(".ico", StringComparison.Ordinal) || rel.EndsWith(".ttf", StringComparison.Ordinal);

    private static bool IsCopiedAsIs(string rel) =>
        IsBinary(rel) || rel.StartsWith("src/Web/wwwroot/lib/", StringComparison.Ordinal) || rel.StartsWith("src/Maui/wwwroot/lib/", StringComparison.Ordinal);

    /// <summary>
    /// The platform's text as starter/ spells it: the brand becomes the placeholder, except where it names what an app keeps
    /// — the map's keep-list (the package ids, the feed key, also as JSON escapes them), the platform repo, and the
    /// namespaces the packages ship (Perezosoft.Core, .Infrastructure, .Api, .Client, .Testing; the platform's test
    /// project Perezosoft.Api.Tests is an app's own). One exception: the native app's id, com.perezosoft.platform, is an
    /// app's com.appbrand.app, not com.appbrand.platform.
    /// </summary>
    public static string ToStarter(string text, IEnumerable<string> keep)
    {
        text = text.Replace("com.perezosoft.platform", "com.appbrand.app", StringComparison.Ordinal);
        var tokens = keep.SelectMany(k => new[] { k, k.Replace("\"", "\\\"", StringComparison.Ordinal) }).Distinct()
            .Select(Regex.Escape).Append("perezosoft-platform")
            .Append(@"Perezosoft\.(?:Core|Infrastructure|Client|Testing)\b").Append(@"Perezosoft\.Api\b(?!\.Tests)");
        var held = new List<string>();
        text = Regex.Replace(text, string.Join('|', tokens), m => { held.Add(m.Value); return $"{held.Count - 1}"; });
        text = text.Replace("Perezosoft", "AppBrand", StringComparison.Ordinal).Replace("perezosoft", "appbrand", StringComparison.Ordinal)
            .Replace("PEREZOSOFT", "APPBRAND", StringComparison.Ordinal);
        return Regex.Replace(text, "(\\d+)", m => held[int.Parse(m.Groups[1].Value)]);
    }

    [Fact]
    public void EveryStraightCopy_IsThePlatformsFile_Renamed()
    {
        var root = RepoRoot();
        var keep = Keep(root);
        var drifted = StraightCopies.Where(path =>
        {
            var starter = Path.Combine(root, "starter", ToStarter(path, keep));
            return !File.Exists(starter)
                   || Text(starter) != ToStarter(Text(Path.Combine(root, path)), keep);
        }).ToList();
        Assert.True(drifted.Count == 0,
            "starter/ copies out of step with the platform's file — copy it again, renamed (StarterTests.ToStarter): " + string.Join(", ", drifted));
    }

    [Fact]
    public void EveryCopiedTree_IsThePlatformsTree_Renamed()
    {
        // Both ways: a file the platform adds, changes or deletes in one of these trees is added, changed or deleted in
        // starter/ in the same commit.
        var root = RepoRoot();
        var keep = Keep(root);
        var drifted = new List<string>();
        foreach (var tree in CopiedTrees)
        {
            var platform = TreeFiles(root, tree).Where(IsCopied).ToDictionary(rel => ToStarter(rel, keep));
            var starter = TreeFiles(Path.Combine(root, "starter"), ToStarter(tree, keep)).Where(IsCopied).ToHashSet();
            drifted.AddRange(starter.Where(rel => !platform.ContainsKey(rel)).Select(rel => $"{rel} (not the platform's)"));
            drifted.AddRange(platform.Where(p => !starter.Contains(p.Key)).Select(p => $"{p.Key} (missing)"));
            drifted.AddRange(platform.Where(p => starter.Contains(p.Key) && !SameCopy(root, p.Value, p.Key, keep)).Select(p => p.Key));
        }
        Assert.True(drifted.Count == 0,
            "starter/ UI trees out of step with the platform's — copy the file again, renamed (StarterTests.ToStarter; binaries and " +
            "src/Web/wwwroot/lib as they are): " + string.Join(", ", drifted));

        // Not the project file, its lockfile, or a developer's own (gitignored) .csproj.user.
        static bool IsCopied(string rel) => !rel.EndsWith(".csproj", StringComparison.Ordinal) && !rel.EndsWith(".csproj.user", StringComparison.Ordinal)
                                            && !rel.EndsWith("packages.lock.json", StringComparison.Ordinal)
                                            && !rel.StartsWith("src/Host/Persistence/Migrations/", StringComparison.Ordinal)
                                            && rel != "docs/postman/README.md";
    }

    [Fact]
    public void TheWebHost_PinsTheSdkPacks_AsThePlatformDoes()
    {
        // The WebAssembly SDK's build-time packs are pinned in the host's project file (VersionOverride), which the trees'
        // copy leaves to the app: a bump in the platform's web host is a bump in the starter's.
        var root = RepoRoot();
        var platform = Overrides(Text(Path.Combine(root, "src", "Web", "Perezosoft.Web.csproj")));
        var starter = Overrides(Text(Path.Combine(root, "starter", "src", "Web", "AppBrand.Web.csproj")));
        Assert.NotEmpty(platform);
        Assert.Equal(platform.OrderBy(p => p.Key), starter.OrderBy(p => p.Key));

        static Dictionary<string, string> Overrides(string csproj) =>
            Regex.Matches(csproj, "<PackageReference Include=\"([^\"]+)\" VersionOverride=\"([^\"]+)\"")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    [Fact]
    public void TheStarter_PinsEveryPackage_AsThePlatformDoes()
    {
        // A third-party bump in the platform reaches the apps `dotnet new` makes from then on.
        var root = RepoRoot();
        var platform = Pins(Text(Path.Combine(root, "Directory.Packages.props")));
        var starter = Pins(Text(Path.Combine(root, "starter", "Directory.Packages.props")));
        var off = platform.Where(p => !starter.TryGetValue(p.Key, out var v) || v != p.Value).Select(p => p.Key).ToList();
        Assert.True(off.Count == 0, "starter/Directory.Packages.props pins these otherwise than the platform: " + string.Join(", ", off));
        Assert.All(starter.Keys.Where(k => k.StartsWith("Perezosoft.Platform.", StringComparison.Ordinal)),
            id => Assert.Equal("$(PlatformPackagesVersion)", starter[id]));
    }

    [Fact]
    public void TheStarter_NamesThePlatformsBrand_OnlyWhereAnAppKeepsIt()
    {
        // The template renames the placeholder; a platform brand left in starter/ would reach every new app as it is. Named
        // on purpose: the platform an app is built on, in its README and the template's own description (never copied into
        // an app), and the stamp's brand map, whose keys are the platform's tokens.
        var root = RepoRoot();
        var keep = Keep(root);
        string[] namesThePlatform = ["README.md", ".template.config/template.json"];
        var named = StarterFiles(root)
            .Where(f => !namesThePlatform.Contains(Path.GetRelativePath(Path.Combine(root, "starter"), f).Replace('\\', '/')))
            .Where(f =>
            {
                var t = Text(f);
                if (Path.GetFileName(f) == "platform-stamp.json")
                    t = t.Replace("\"Perezosoft\": \"AppBrand\", \"perezosoft\": \"appbrand\"", "", StringComparison.Ordinal);
                return ToStarter(t, keep) != t;
            })
            .Select(f => Path.GetRelativePath(root, f)).ToList();
        Assert.True(named.Count == 0, "starter/ files naming Perezosoft outside the keep-list: " + string.Join(", ", named));
    }

    [Fact]
    public void TheTemplate_CopiesTheRegister_AndBakesTheVersion_WhereTheAppNamesIt()
    {
        var root = RepoRoot();
        using var template = JsonDocument.Parse(Text(Path.Combine(root, "starter", ".template.config", "template.json")));
        Assert.Equal("AppBrand", template.RootElement.GetProperty("sourceName").GetString());
        Assert.Equal("perezosoft-app", template.RootElement.GetProperty("shortName").GetString());

        // The register is copied as it is (the engine evaluates MSBuild conditions it can, and dropped one of the switch's),
        // and so are the binaries and the vendored Bootstrap: copy-only files are never renamed, so they must not name the
        // placeholder either.
        var copyOnly = template.RootElement.GetProperty("sources")[0].GetProperty("copyOnly").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(StraightCopies[2..8].Concat(CopiedAsIs).Order(), copyOnly.Order());
        var starterRoot = Path.Combine(root, "starter");
        Assert.All(StarterFiles(root).Where(f =>
            {
                var rel = Path.GetRelativePath(starterRoot, f).Replace('\\', '/');
                return copyOnly.Contains(rel) || IsCopiedAsIs(rel);
            }),
            f => Assert.DoesNotContain("AppBrand", Text(f), StringComparison.OrdinalIgnoreCase));

        // Everywhere else, a condition the engine could evaluate is one it may rewrite: each must read a property.
        var constant = StarterFiles(root)
            .Where(f => f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal) || f.EndsWith("proj", StringComparison.Ordinal))
            .Where(f => !copyOnly.Contains(Path.GetRelativePath(Path.Combine(root, "starter"), f).Replace('\\', '/')))
            .SelectMany(f => Regex.Matches(Text(f), "Condition=\"([^\"]*)\"").Where(m => !m.Groups[1].Value.Contains("$(", StringComparison.Ordinal))
                .Select(m => $"{Path.GetRelativePath(root, f)}: {m.Value}")).ToList();
        Assert.True(constant.Count == 0, "conditions the template engine would evaluate: " + string.Join("; ", constant));

        // 0.0.0-platform is the release the template is packed as (Perezosoft.Platform.Templates bakes it): the package
        // version, the CI caller's tag, the stamp's commit — and nowhere else.
        var baked = StarterFiles(root).Where(f => Text(f).Contains("0.0.0-platform", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Path.Combine(root, "starter"), f).Replace('\\', '/')).Order().ToList();
        Assert.Equal([".github/workflows/ci.yml", "Directory.Packages.props", "platform-stamp.json"], baked);
        Assert.True(IsOnThePackages(Text(Path.Combine(root, "starter", "platform-stamp.json"))));
    }

    [Fact]
    public void EveryPreprocessorDirective_IsFencedFromTheTemplateEngine()
    {
        // dotnet new reads a C# #if as its own condition: DEBUG, ANDROID and the rest are no symbols of the template, so
        // it dropped every such block from the templated MauiProgram (Pkg 6.1c). The engine leaves a region alone between
        // its two markers, which it does not write out; the platform's file carries them, so its copy here does too.
        var root = RepoRoot();
        var open = string.Concat("//-", ":cnd:noEmit");
        var close = string.Concat("//+", ":cnd:noEmit");
        var unfenced = StarterFiles(root).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)).SelectMany(f =>
        {
            var fenced = false;
            return Text(f).Split('\n').Select((line, i) => (line: line.Trim(), i)).Where(l =>
            {
                if (l.line == open) fenced = true;
                else if (l.line == close) fenced = false;
                return !fenced && Regex.IsMatch(l.line, @"^#(if|elif)\b");
            }).Select(l => $"{Path.GetRelativePath(root, f)}:{l.i + 1}");
        }).ToList();
        Assert.True(unfenced.Count == 0, "#if outside the template engine's fence (the two cnd:noEmit marker lines): " + string.Join(", ", unfenced));
    }

    private static IReadOnlyList<string> Keep(string root) => ParseKeep(Text(Path.Combine(root, "platform-ownership.json")));

    private static Dictionary<string, string> Pins(string props) =>
        Regex.Matches(props, "<PackageVersion Include=\"([^\"]+)\" Version=\"([^\"]+)\"")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    private static IEnumerable<string> StarterFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "starter"), "*", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f.Replace('\\', '/'), "/(bin|obj|\\.vs|node_modules)/") && !f.EndsWith("packages.lock.json", StringComparison.Ordinal)
                        && Path.GetFileName(f) != ".env");

    // A tree's files, repo-relative with forward slashes, without build output.
    private static IEnumerable<string> TreeFiles(string root, string tree) =>
        Directory.EnumerateFiles(Path.Combine(root, tree), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => !Regex.IsMatch(rel, "/(bin|obj|\\.vs|TestResults|node_modules)/"));

    private static bool SameCopy(string root, string platformRel, string starterRel, IReadOnlyList<string> keep)
    {
        var platform = Path.Combine(root, platformRel);
        var starter = Path.Combine(root, "starter", starterRel);
        if (IsBinary(starterRel))
            return File.ReadAllBytes(platform).AsSpan().SequenceEqual(File.ReadAllBytes(starter));
        // Text, Bootstrap's included: compared as git checks it out (line endings), renamed unless it is copied as it is.
        return Text(starter) == (IsCopiedAsIs(starterRel) ? Text(platform) : ToStarter(Text(platform), keep));
    }

    // Line endings are git's (autocrlf on Windows), the same on both sides of any comparison here.
    private static string Text(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
