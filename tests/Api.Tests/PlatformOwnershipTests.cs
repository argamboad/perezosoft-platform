using System.Diagnostics;
using System.Text;
using Perezosoft.Testing.Suites;
using Xunit.Abstractions;
using static Perezosoft.Testing.Architecture.PlatformOwnership;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Arch A2 (#363), R160: the platform/app boundary has a map, a stamp and, in a clone, a manifest.
/// <c>platform-ownership.json</c> classes every tracked file (platform / register / seed / app / sample / producer, Pkg 6.4).
/// An app on the packages is held to the register the Testing package embeds (<see cref="PlatformRegisterTests"/>), and
/// <c>tools/port-platform.ps1</c> merges its register from starter/ at one release to the next. A clone's run of the tool
/// writes <c>tests/Api.Tests/App/platform-manifest.json</c> (the brand-normalised hash of every register and seed file at
/// the platform commit) and <c>platform-stamp.json</c>. On the platform the stamp's commit is null and only the map gate
/// applies. The logic is <see cref="PlatformOwnership"/>, held on fixtures here, and the tool is run for real against
/// scratch git repos in both modes, so the PowerShell hash and rename and the C# ones are pinned equal. The repo checks are
/// <see cref="PlatformOwnershipSuite"/> (Pkg 5.1b), which every app inherits; the self-tests stay here.
/// </summary>
public class PlatformOwnershipTests(ITestOutputHelper output) : PlatformOwnershipSuite(output)
{
    [Fact]
    public void ProducerFiles_ExistInThePlatform_AndNowhereElseAreDemanded()
    {
        // The other half of the producer class: the platform must still carry every producer file its map names (the gates
        // that read them skip only where they are absent, i.e. downstream), and a manifest never lists one.
        var root = RepoRoot();
        var rules = ParseRules(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        if (!IsThePlatform(root)) return;
        var tracked = Git(root, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        Assert.Contains(tracked, p => Classify(p, rules) == Class.Producer && p == ".github/workflows/release.yml");
        var manifest = Manifest((".github/workflows/release.yml", Class.Producer, "x"), ("src/Core/A.cs", Class.Register, "a"));
        var v = Evaluate(manifest, "abc", "abc", new Dictionary<string, string>(), Brand, Repo(("src/Core/A.cs", "a")));
        Assert.Empty(v.Failures);                          // an absent producer file is never "a platform file this repo does not have"
        Assert.Equal(1, v.RegisterFilesChecked);
    }

    [Fact]
    public void LocalPath_KeepsTheKeepList()
    {
        Assert.Equal("tools/Acme.Tool/Perezosoft.Platform.Core.csproj",
            LocalPath("tools/Perezosoft.Tool/Perezosoft.Platform.Core.csproj", Brand, ["Perezosoft.Platform."]));
    }

    // ── the logic, on fixtures ──

    [Theory]
    [InlineData("src/Core/**", "src/Core/Entities/User.cs", true)]
    [InlineData("src/Core/**", "src/Core/User.cs", true)]
    [InlineData("src/Core/**", "src/Corex/User.cs", false)]
    [InlineData("**/packages.lock.json", "packages.lock.json", true)]
    [InlineData("**/packages.lock.json", "src/Api/packages.lock.json", true)]
    [InlineData("src/Api/*.csproj", "src/Api/Perezosoft.Api.csproj", true)]
    [InlineData("src/Api/*.csproj", "src/Api/Sub/Perezosoft.Api.csproj", false)]
    [InlineData("docs/audits/**/FOUNDATION_RULES*.md", "docs/audits/v4-2026-09/FOUNDATION_RULES_v3.md", true)]
    [InlineData("README.md", "docs/README.md", false)]
    public void Globs_AreGitStyle(string glob, string path, bool matches) => Assert.Equal(matches, GlobToRegex(glob).IsMatch(path));

    [Fact]
    public void NormalizedHash_IgnoresBrandLineEndingsBomAndTrailingWhitespace_ButNotContent()
    {
        var platform = Encoding.UTF8.GetBytes("namespace Perezosoft.Api;\r\nclass Perezosoft { } // perezosoft  \r\n");
        var app = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("namespace Perezosoft.Api;\nclass Perezosoft { } // perezosoft\n\n")).ToArray();
        Assert.Equal(NormalizedHash(platform, ["Perezosoft", "perezosoft"]), NormalizedHash(app, ["Perezosoft", "perezosoft"]));
        var changed = Encoding.UTF8.GetBytes("namespace Perezosoft.Api;\nclass Perezosoft { int x; } // perezosoft\n");
        Assert.NotEqual(NormalizedHash(platform, ["Perezosoft", "perezosoft"]), NormalizedHash(changed, ["Perezosoft", "perezosoft"]));
        // A binary hashes raw: the brand map never touches it.
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 13 };
        Assert.Equal(NormalizedHash(png, ["Perezosoft"]), NormalizedHash(png, ["Perezosoft"]));
    }

    private static readonly Dictionary<string, string> Brand = new() { ["Perezosoft"] = "Acme", ["perezosoft"] = "acme" };

    private static List<ManifestEntry> Manifest(params (string Path, Class Class, string Content)[] files) =>
        files.Select(f => new ManifestEntry(f.Path, f.Class, NormalizedHash(Encoding.UTF8.GetBytes(f.Content), ["Perezosoft", "perezosoft"]))).ToList();

    private static Func<string, byte[]?> Repo(params (string Path, string Content)[] files)
    {
        var d = files.ToDictionary(f => f.Path, f => Encoding.UTF8.GetBytes(f.Content), StringComparer.Ordinal);
        return p => d.TryGetValue(p, out var b) ? b : null;
    }

    [Fact]
    public void Verdict_PassesAnUnchangedRepo_AndReportsSeedDrift()
    {
        var manifest = Manifest(("src/Core/A.cs", Class.Register, "class Perezosoft {}"), ("src/Shared.Ui/P.razor", Class.Seed, "<h1>Perezosoft</h1>"), ("src/Api/Features/X.cs", Class.App, "x"));
        var repo = Repo(("src/Core/A.cs", "class Acme {}"), ("src/Shared.Ui/P.razor", "<h1>Acme, the budget app</h1>"));
        var v = Evaluate(manifest, "abc", "abc", new Dictionary<string, string>(), Brand, repo);
        Assert.Empty(v.Failures);
        Assert.Equal(["src/Shared.Ui/P.razor"], v.SeedDrift);
        Assert.Equal(1, v.RegisterFilesChecked);
    }

    [Fact]
    public void Verdict_FailsAChangedRegisterFile_UnlessAllowlisted_AndFailsAnAllowlistThatRots()
    {
        var manifest = Manifest(("src/Core/Perezosoft.Core.csproj", Class.Register, "<Project/>"), ("src/Core/A.cs", Class.Register, "class Perezosoft {}"));
        var changed = Repo(("src/Core/Acme.Core.csproj", "<Project/>"), ("src/Core/A.cs", "class Acme { int x; }"));
        var none = new Dictionary<string, string>();
        Assert.Contains(Evaluate(manifest, "abc", "abc", none, Brand, changed).Failures, f => f.StartsWith("src/Core/A.cs: differs"));

        var allowlisted = new Dictionary<string, string> { ["src/Core/A.cs"] = "the app adds x (ADR-V001)" };
        Assert.Empty(Evaluate(manifest, "abc", "abc", allowlisted, Brand, changed).Failures);

        var identical = Repo(("src/Core/Acme.Core.csproj", "<Project/>"), ("src/Core/A.cs", "class Acme {}"));
        Assert.Contains(Evaluate(manifest, "abc", "abc", allowlisted, Brand, identical).Failures, f => f.Contains("identical to the platform again"));

        var missing = Repo(("src/Core/Acme.Core.csproj", "<Project/>"));
        Assert.Contains(Evaluate(manifest, "abc", "abc", none, Brand, missing).Failures, f => f.Contains("does not have"));

        var blankReason = new Dictionary<string, string> { ["src/Core/A.cs"] = "" };
        Assert.Contains(Evaluate(manifest, "abc", "abc", blankReason, Brand, changed).Failures, f => f.StartsWith("src/Core/A.cs: differs"));
    }

    [Fact]
    public void Verdict_FailsAStampThatDisagreesWithTheManifest_AndADivergenceForAnUnknownFile()
    {
        var manifest = Manifest(("src/Core/A.cs", Class.Register, "a"));
        var repo = Repo(("src/Core/A.cs", "a"));
        Assert.Contains(Evaluate(manifest, "abc", "def", new Dictionary<string, string>(), Brand, repo).Failures, f => f.Contains("a port must update both"));
        var orphan = new Dictionary<string, string> { ["src/Api/Features/X.cs"] = "ours" };
        Assert.Contains(Evaluate(manifest, "abc", "abc", orphan, Brand, repo).Failures, f => f.Contains("the manifest does not know"));
    }

    // ── the tool, for real ──

    [Fact]
    public async Task PortTool_OnAClone_MergesRegisterAndSeedFiles_AndWritesTheManifestTheGateAccepts()
    {
        // Two scratch repos: a "platform" with two commits and an "app" cloned from the first with the brand renamed and
        // two local edits. The port to the second commit must bring the platform's changes, keep the app's, write the
        // manifest and the stamp — and the gate, computing the hash in C#, must accept what the tool hashed in PowerShell.
        // Packaged source (class platform) is never ported into a clone: a clone converts to the packages instead (#423).
        using var scratch = new Scratch();
        var platform = scratch.Dir("platform");
        var app = scratch.Dir("app");
        const string map = """
            { "rules": [
              { "glob": "src/Api/Features/**", "class": "app" },
              { "glob": "platform-stamp.json", "class": "app" },
              { "glob": "src/Core/Release.yml", "class": "producer" },
              { "glob": "src/Packaged/**", "class": "platform" },
              { "glob": "src/Core/**", "class": "register" },
              { "glob": "src/Shared.Ui/**", "class": "seed" },
              { "glob": "platform-ownership.json", "class": "register" },
              { "glob": "README.md", "class": "seed" },
              { "glob": "logo.bin", "class": "seed" } ],
              "keep": [ "Perezosoft.Platform.", "key=\"perezosoft\"" ] }
            """;
        Write(platform, "platform-ownership.json", map);
        Write(platform, "platform-stamp.json", """{ "platform": "owner/perezosoft-platform", "commit": null, "date": null, "brand": { "Perezosoft": "Perezosoft", "perezosoft": "perezosoft" } }""");
        Write(platform, "src/Core/Perezosoft.Core.csproj", "<Project>v1</Project>\n");
        Write(platform, "src/Core/Thing.cs", "namespace Perezosoft.Core;\n\npublic class Thing\n{\n    public int A = 1;\n}\n");
        Write(platform, "src/Shared.Ui/Page.razor", "<h1>Perezosoft</h1>\n<p>line two</p>\n<p>line three</p>\n");
        Write(platform, "src/Api/Features/X/X.cs", "// platform sample\n");
        Write(platform, "README.md", "# Perezosoft\n");
        File.WriteAllBytes(Path.Combine(platform, "logo.bin"), [0x89, 0x50, 0, 1, 2]);
        Git(platform, "init", "-q", "-b", "main");
        Git(platform, "add", "-A");
        Git(platform, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "v1");
        var v1 = Git(platform, "rev-parse", "HEAD").Trim();

        // The app: v1 with the brand renamed in content and file names, a stamp at v1, and its own edits to an adapts file and an app file.
        foreach (var f in Directory.EnumerateFiles(platform, "*", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}")))
        {
            var rel = Path.GetRelativePath(platform, f).Replace('\\', '/');
            if (rel.EndsWith("Release.yml", StringComparison.Ordinal)) continue; // producer: an app never has it
            var target = Path.Combine(app, ToAcme(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (rel == "logo.bin") File.Copy(f, target);
            else File.WriteAllText(target, ToAcme(File.ReadAllText(f)));
        }
        Write(app, "platform-stamp.json", $$"""{ "platform": "owner/perezosoft-platform", "commit": "{{v1}}", "date": "2026-10-01", "brand": { "Perezosoft": "Acme", "perezosoft": "acme" } }""");
        Write(app, "src/Shared.Ui/Page.razor", "<h1>Acme, the budget app</h1>\n<p>line two</p>\n<p>line three</p>\n");
        Write(app, "src/Api/Features/X/X.cs", "// the app's own slice\n");
        Directory.CreateDirectory(Path.Combine(app, "tools")); // the script locates the repo from tools/ unless -Repo is given

        // Platform v2: a platform file changes, a platform file is added, an adapts file changes elsewhere than the app's edit,
        // the sample-ish app file changes (must not reach the app), a binary changes.
        Write(platform, "src/Core/Thing.cs", "namespace Perezosoft.Core;\n\npublic class Thing\n{\n    public int A = 2;\n    public int B = 3;\n}\n");
        Write(platform, "src/Core/Added.cs", "namespace Perezosoft.Core;\n\npublic class Added;\n");
        Write(platform, "src/Packaged/Lib.cs", "namespace Perezosoft.Packaged;\n");
        // Pkg 2.5: a platform package id and a feed key the brand map must never rename, and a producer-only file (the
        // platform's release machinery) the port must never bring.
        Write(platform, "src/Core/Feed.config", "<add key=\"perezosoft\" /> <package pattern=\"Perezosoft.Platform.*\" /> Perezosoft.Api perezosoft-x\n");
        Write(platform, "src/Core/Release.yml", "publishes Perezosoft.Platform.Core\n");
        // A file that itself holds a private-use character (as this very test file once did) must not be corrupted by the
        // guards the rename parks kept tokens behind.
        Write(platform, "src/Core/Guarded.txt", (char)0xE000 + " and " + (char)0xE001 + " stay; Perezosoft.Platform.Core stays; Perezosoft goes\n");
        Write(platform, "src/Shared.Ui/Page.razor", "<h1>Perezosoft</h1>\n<p>line two</p>\n<p>line three, revised</p>\n");
        Write(platform, "src/Api/Features/X/X.cs", "// platform sample, changed\n");
        File.WriteAllBytes(Path.Combine(platform, "logo.bin"), [0x89, 0x50, 0, 9, 9]);
        Git(platform, "add", "-A");
        Git(platform, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "v2");
        var v2 = Git(platform, "rev-parse", "HEAD").Trim();

        var (exit, log) = await RunToolAsync(platform, v2, app);
        Assert.True(exit == 0, log);

        Assert.Equal("namespace Acme.Core;\n\npublic class Thing\n{\n    public int A = 2;\n    public int B = 3;\n}\n", Read(app, "src/Core/Thing.cs"));
        Assert.Equal("namespace Acme.Core;\n\npublic class Added;\n", Read(app, "src/Core/Added.cs"));
        Assert.Equal("<h1>Acme, the budget app</h1>\n<p>line two</p>\n<p>line three, revised</p>\n", Read(app, "src/Shared.Ui/Page.razor")); // both edits, no markers
        Assert.Equal("// the app's own slice\n", Read(app, "src/Api/Features/X/X.cs"));                                                 // app class: untouched
        Assert.Equal(new byte[] { 0x89, 0x50, 0, 9, 9 }, File.ReadAllBytes(Path.Combine(app, "logo.bin")));                              // binary, unchanged locally: taken
        // The keep-list: the package id and the feed key stay the platform's; everything else is renamed as before.
        Assert.Equal("<add key=\"perezosoft\" /> <package pattern=\"Perezosoft.Platform.*\" /> Acme.Api acme-x\n", Read(app, "src/Core/Feed.config"));
        Assert.False(File.Exists(Path.Combine(app, "src/Core/Release.yml")), "a producer-only file reached the app");          // producer: never ported
        Assert.False(File.Exists(Path.Combine(app, "src/Packaged/Lib.cs")), "packaged source reached a clone");                 // platform: never ported
        Assert.Equal((char)0xE000 + " and " + (char)0xE001 + " stay; Perezosoft.Platform.Core stays; Acme goes\n", Read(app, "src/Core/Guarded.txt"));

        var (stampCommit, brand) = ParseStamp(Read(app, "platform-stamp.json"));
        Assert.Equal(v2, stampCommit);
        var (manifestCommit, entries) = ParseManifest(Read(app, "tests/Api.Tests/App/platform-manifest.json"));
        Assert.Equal(v2, manifestCommit);
        Assert.Equal(new[] { "README.md", "logo.bin", "platform-ownership.json", "src/Core/Added.cs", "src/Core/Feed.config", "src/Core/Guarded.txt", "src/Core/Perezosoft.Core.csproj", "src/Core/Thing.cs", "src/Shared.Ui/Page.razor" }.Order(StringComparer.Ordinal), // the brand sets the order; no producer entry
            entries.Select(e => e.Path).Order(StringComparer.Ordinal));

        // The gate over the ported app: the PowerShell hashes must match the C# hashes of the renamed files.
        var verdict = Evaluate(entries, manifestCommit, stampCommit, new Dictionary<string, string>(), brand,
            local => File.Exists(Path.Combine(app, local)) ? File.ReadAllBytes(Path.Combine(app, local)) : null);
        Assert.True(verdict.Failures.Count == 0, string.Join("\n", verdict.Failures));
        Assert.Equal(["src/Shared.Ui/Page.razor"], verdict.SeedDrift); // the app's own edit, reported not failed
        Assert.All(entries, e => Assert.Contains(e.Class, new[] { Class.Register, Class.Seed }));

        // An edit to a register file after the port is caught; allowlisting it with a reason clears it.
        Write(app, "src/Core/Thing.cs", Read(app, "src/Core/Thing.cs").Replace("B = 3", "B = 4"));
        var caught = Evaluate(entries, manifestCommit, stampCommit, new Dictionary<string, string>(), brand, local => File.Exists(Path.Combine(app, local)) ? File.ReadAllBytes(Path.Combine(app, local)) : null);
        Assert.Contains(caught.Failures, f => f.StartsWith("src/Core/Thing.cs: differs"));
        var cleared = Evaluate(entries, manifestCommit, stampCommit, new Dictionary<string, string> { ["src/Core/Thing.cs"] = "B is 4 here (ADR-A001)" }, brand, local => File.Exists(Path.Combine(app, local)) ? File.ReadAllBytes(Path.Combine(app, local)) : null);
        Assert.Empty(cleared.Failures);
    }

    [Fact]
    public async Task PortTool_WithoutApply_WritesNothing_AndRefusesAnUnknownCommit()
    {
        using var scratch = new Scratch();
        var platform = scratch.Dir("platform");
        var app = scratch.Dir("app");
        Write(platform, "platform-ownership.json", """{ "rules": [ { "glob": "platform-stamp.json", "class": "app" }, { "glob": "**", "class": "register" } ] }""");
        Write(platform, "platform-stamp.json", """{ "platform": "p", "commit": null, "date": null, "brand": { "Perezosoft": "Perezosoft" } }""");
        Write(platform, "a.txt", "one\n");
        Git(platform, "init", "-q", "-b", "main"); Git(platform, "add", "-A"); Git(platform, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "v1");
        var v1 = Git(platform, "rev-parse", "HEAD").Trim();
        Write(app, "platform-stamp.json", """{ "platform": "p", "commit": null, "date": null, "brand": { "Perezosoft": "Acme" } }""");
        Directory.CreateDirectory(Path.Combine(app, "tools"));

        var (dryExit, dryLog) = await RunToolAsync(platform, v1, app, apply: false);
        Assert.True(dryExit == 0, dryLog);
        Assert.Contains("Dry run", dryLog);
        Assert.False(File.Exists(Path.Combine(app, "a.txt")), "a dry run wrote a file");
        Assert.False(File.Exists(Path.Combine(app, "tests", "Api.Tests", "App", "platform-manifest.json")), "a dry run wrote the manifest");

        var (badExit, badLog) = await RunToolAsync(platform, "no-such-commit", app, apply: false);
        Assert.NotEqual(0, badExit);
        Assert.Contains("is not a commit", badLog);
    }

    [Fact]
    public async Task PortTool_OnThePackages_MergesTheRegisterFromTheStarter_AndListsWhatChangedInTheSeed()
    {
        // Pkg 6.4 (#423). A scratch platform whose starter/ has two releases (tags v1.0.0, v1.1.0), and an app made from the
        // first as dotnet new makes it — the placeholder renamed, the stamp on the packages — with an edit of its own to a
        // register file and to a seed file, and leftovers of a clone (packaged source, release machinery). The port to the
        // release its Directory.Packages.props names must merge the register (both edits kept), write no seed file and list
        // what changed in the seed, list the leftovers for removal, write no manifest, stamp the version — and the gate the
        // app inherits must accept the result, the app's own register edit once it is allowlisted.
        using var scratch = new Scratch();
        var platform = scratch.Dir("platform");
        var app = scratch.Dir("app");
        const string map = """
            { "rules": [
              { "glob": "starter/**", "class": "producer" },
              { "glob": "tools/release/**", "class": "producer" },
              { "glob": "src/Core/**", "class": "platform" },
              { "glob": "src/Host/Features/**", "class": "app" },
              { "glob": "platform-stamp.json", "class": "app" },
              { "glob": "src/Host/Program.cs", "class": "register" },
              { "glob": "tests/Api.Tests/Infrastructure/**", "class": "register" },
              { "glob": "global.json", "class": "register" },
              { "glob": "platform-ownership.json", "class": "register" },
              { "glob": "**", "class": "seed" } ],
              "keep": [ "Perezosoft.Platform.", "key=\"perezosoft\"" ] }
            """;
        Write(platform, "platform-ownership.json", map);
        Write(platform, "starter/platform-ownership.json", map);
        Write(platform, "starter/.template.config/template.json", """{ "sourceName": "AppBrand", "shortName": "perezosoft-app" }""");
        Write(platform, "starter/platform-stamp.json", """{ "platform": "owner/perezosoft-platform", "commit": "0.0.0-platform", "packages": true, "brand": { "Perezosoft": "AppBrand", "perezosoft": "appbrand" } }""");
        Write(platform, "starter/global.json", "{ \"sdk\": \"10.0.401\" }\n");
        Write(platform, "starter/src/Host/Program.cs", "using Perezosoft.Infrastructure;\nnamespace AppBrand.Host;\n// line two\n// line three\n// line four\n");
        Write(platform, "starter/src/Host/Features/Budget/Budget.cs", "namespace AppBrand.Host.Features.Budget;\n");
        Write(platform, "starter/src/Web/Page.razor", "<h1>AppBrand</h1>\n");
        Write(platform, "starter/src/Web/Old.razor", "<p>old</p>\n");
        Git(platform, "init", "-q", "-b", "main");
        Git(platform, "add", "-A");
        Git(platform, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "1.0.0");
        var v1 = Git(platform, "rev-parse", "HEAD").Trim();
        Git(platform, "tag", "v1.0.0");

        // The app, as dotnet new perezosoft-app -n Acme wrote it from 1.0.0 — the stamp still says what the template was
        // packed as (0.0.0-platform, which has no tag) — then its own edits, and a clone's leftovers.
        foreach (var rel in Git(platform, "ls-files", "starter").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()[8..]))
            if (!rel.StartsWith(".template.config/", StringComparison.Ordinal))
                Write(app, ToAcmeApp(rel), ToAcmeApp(Read(platform, "starter/" + rel)));
        Write(app, "Directory.Packages.props", "<Project><PropertyGroup><PlatformPackagesVersion>1.1.0</PlatformPackagesVersion></PropertyGroup></Project>\n");
        Write(app, "src/Host/Program.cs", "using Perezosoft.Infrastructure;\nnamespace Acme.Host;\n// line two, the app's\n// line three\n// line four\n");
        Write(app, "src/Web/Page.razor", "<h1>Acme, the budget app</h1>\n");
        Write(app, "src/Core/Old.cs", "namespace Perezosoft.Core;\n");
        Write(app, "tools/release/pack.ps1", "# the publisher's\n");
        Git(app, "init", "-q", "-b", "main");
        Git(app, "add", "-A");

        // 1.1.0: the register moves (a line of the entry point, the SDK pin, a new fixture), and so does the seed.
        Write(platform, "starter/src/Host/Program.cs", "using Perezosoft.Infrastructure;\nnamespace AppBrand.Host;\n// line two\n// line three\n// line four, revised\n");
        Write(platform, "starter/global.json", "{ \"sdk\": \"10.0.402\" }\n");
        Write(platform, "starter/tests/Api.Tests/Infrastructure/Fixture.cs", "namespace AppBrand.Api.Tests.Infrastructure; // APPBRAND_DB, Perezosoft.Platform.Testing\n");
        Write(platform, "starter/src/Web/Page.razor", "<h1>AppBrand</h1>\n<p>new</p>\n");
        Write(platform, "starter/src/Web/New.razor", "<p>AppBrand</p>\n");
        File.Delete(Path.Combine(platform, "starter/src/Web/Old.razor"));
        Git(platform, "add", "-A");
        Git(platform, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "1.1.0");
        var v2 = Git(platform, "rev-parse", "HEAD").Trim();
        Git(platform, "tag", "v1.1.0");

        // Without a tag for the app's version, nothing is a base: the run says so (and -From names one).
        var (noBaseExit, noBaseLog) = await RunToolAsync(platform, null, app, apply: false);
        Assert.True(noBaseExit == 0, noBaseLog);
        Assert.Contains("No release tag v0.0.0-platform", noBaseLog);
        Assert.Contains("CONFLICT (no base", noBaseLog);
        Assert.Equal("0.0.0-platform", ParseStamp(Read(app, "platform-stamp.json")).Commit); // a dry run wrote nothing

        var (exit, log) = await RunToolAsync(platform, null, app, from: v1);
        Assert.True(exit == 0, log);

        // The register: merged, both edits kept, the placeholder renamed and the packages' names kept; a new file added.
        Assert.Equal("using Perezosoft.Infrastructure;\nnamespace Acme.Host;\n// line two, the app's\n// line three\n// line four, revised\n", Read(app, "src/Host/Program.cs"));
        Assert.Equal("{ \"sdk\": \"10.0.402\" }\n", Read(app, "global.json"));
        Assert.Equal("namespace Acme.Api.Tests.Infrastructure; // ACME_DB, Perezosoft.Platform.Testing\n", Read(app, "tests/Api.Tests/Infrastructure/Fixture.cs"));
        // The seed: never written, each change listed.
        Assert.Equal("<h1>Acme, the budget app</h1>\n", Read(app, "src/Web/Page.razor"));
        Assert.False(File.Exists(Path.Combine(app, "src/Web/New.razor")), "the port wrote a seed file");
        Assert.True(File.Exists(Path.Combine(app, "src/Web/Old.razor")), "the port deleted a seed file");
        Assert.Matches(@"seed: changed upstream\s+src/Web/Page\.razor", log);
        Assert.Matches(@"seed: added upstream\s+src/Web/New\.razor", log);
        Assert.Matches(@"seed: deleted upstream\s+src/Web/Old\.razor", log);
        // What an app on the packages must not carry, listed for removal; nothing deleted.
        Assert.Matches(@"packaged: remove\s+src/Core/Old\.cs", log);
        Assert.Matches(@"producer-only: remove\s+tools/release/pack\.ps1", log);
        Assert.True(File.Exists(Path.Combine(app, "src/Core/Old.cs")));
        // No manifest (the package is one); the stamp names the release.
        Assert.False(File.Exists(Path.Combine(app, "tests", "Api.Tests", "App", "platform-manifest.json")), "the port wrote a manifest for an app on the packages");
        var stamp = Read(app, "platform-stamp.json");
        var (version, brand) = ParseStamp(stamp);
        Assert.Equal("1.1.0", version);
        Assert.True(IsOnThePackages(stamp));
        Assert.Equal("Acme", brand["Perezosoft"]);

        // The gate the app inherits, over the register 1.1.0 embeds: the app's edit to the entry point is its divergence.
        var rules = ParseRules(map);
        var register = Git(platform, "ls-tree", "-r", "--name-only", v2, "starter/").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()[8..]).Where(rel => Classify(rel, rules) == Class.Register)
            .ToDictionary(rel => rel, rel => Encoding.UTF8.GetBytes(Git(platform, "show", $"{v2}:starter/{rel}")));
        Assert.Equal(4, register.Count);
        byte[]? ReadApp(string local) => File.Exists(Path.Combine(app, local)) ? File.ReadAllBytes(Path.Combine(app, local)) : null;
        var verdict = EvaluateRegister(register, brand, new Dictionary<string, string>(), ReadApp, ParseKeep(map));
        Assert.Equal(["src/Host/Program.cs"], verdict.Failures.Select(f => f[..f.IndexOf(':')]));
        Assert.Empty(EvaluateRegister(register, brand, new Dictionary<string, string> { ["src/Host/Program.cs"] = "the app's line two (ADR-A001)" }, ReadApp, ParseKeep(map)).Failures);

        var (againExit, againLog) = await RunToolAsync(platform, null, app, apply: false);
        Assert.True(againExit == 0, againLog);
        Assert.Contains("Already on platform 1.1.0", againLog);
        var (badExit, badLog) = await RunToolAsync(platform, "9.9.9", app, apply: false);
        Assert.NotEqual(0, badExit);
        Assert.Contains("is not a release tag", badLog);
    }

    // ── helpers ──

    // The first clone, as a rebrand that honours the keep-list (and the platform's repo name) would have written it. The
    // guards are built in code: a literal private-use character in this file is exactly what tripped the port tool once.
    private static readonly string G0 = ((char)0xF8F0).ToString(), G1 = ((char)0xF8F1).ToString(), G2 = ((char)0xF8F2).ToString();

    private static string ToAcme(string text) => text
        .Replace("perezosoft-platform", G0).Replace("Perezosoft.Platform.", G1).Replace("key=\"perezosoft\"", G2)
        .Replace("Perezosoft", "Acme").Replace("perezosoft", "acme")
        .Replace(G0, "perezosoft-platform").Replace(G1, "Perezosoft.Platform.").Replace(G2, "key=\"perezosoft\"");

    // The starter's placeholder as dotnet new renames it for an app called Acme.
    private static string ToAcmeApp(string text) => text.Replace("AppBrand", "Acme").Replace("APPBRAND", "ACME").Replace("appbrand", "acme");

    private static async Task<(int Exit, string Output)> RunToolAsync(string platform, string? to, string repo, bool apply = true, string? from = null)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-NoProfile", "-File", Path.Combine(RepoRoot(), "tools", "port-platform.ps1"), "-Platform", platform, "-Repo", repo })
            start.ArgumentList.Add(a);
        if (to is not null) { start.ArgumentList.Add("-To"); start.ArgumentList.Add(to); }
        if (from is not null) { start.ArgumentList.Add("-From"); start.ArgumentList.Add(from); }
        if (apply) start.ArgumentList.Add("-Apply");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh is not on PATH — the tools/ scripts require PowerShell 7");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void Write(string root, string rel, string content)
    {
        var full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel)).Replace("\r\n", "\n");

    private static new string Git(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(repo);
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {e}");
        return o;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ownership-" + Guid.NewGuid().ToString("N"));
        public string Dir(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* a scratch dir; the OS temp sweep gets it */ } catch (UnauthorizedAccessException) { }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
