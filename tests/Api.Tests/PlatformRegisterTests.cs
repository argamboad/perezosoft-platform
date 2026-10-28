using System.Diagnostics;
using System.Text;
using static Perezosoft.Testing.Architecture.PlatformOwnership;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 5.7 (#450), 6.4 (#423): an app on the packages keeps no port and no manifest; its register-class files are held to
/// the copies the platform package it references embeds — the starter's, spelled with its placeholder brand. These hold the
/// register here, in the platform: that the embedded list is the map's register over starter/, that the platform's own
/// register files are all in it, that an app's copy compares equal as dotnet new wrote it, and the verdict on fixtures.
/// </summary>
public class PlatformRegisterTests
{
    [Fact]
    public void TheRegister_IsEveryRegisterFileOfTheStarter_AsTheStarterSpellsIt()
    {
        // One list: src/Testing's csproj embeds what the map classes register in starter/, and nothing else.
        var root = RepoRoot();
        var rules = ParseRules(File.ReadAllText(Path.Combine(root, "starter", "platform-ownership.json")));
        var starter = Tracked(root, "starter").Where(p => !p.StartsWith(".template.config/", StringComparison.Ordinal)).ToList();
        var register = starter.Where(p => Classify(p, rules) == Class.Register).Order(StringComparer.Ordinal).ToList();
        var embedded = RegisterFiles();
        Assert.True(register.Count >= 20, $"probe: {register.Count} register files in starter/");
        Assert.Equal(register, embedded.Keys.Order(StringComparer.Ordinal));
        foreach (var path in register)
            Assert.Equal(Normalize(File.ReadAllBytes(Path.Combine(root, "starter", path)), []), Normalize(embedded[path], []));
        // starter/ carries no packaged source and no release machinery: the app `dotnet new` writes takes those as packages.
        var notAnApps = starter.Where(p => Classify(p, rules) is Class.Platform or Class.Producer or null).ToList();
        Assert.True(notAnApps.Count == 0, "starter/ files the map classes platform or producer (or not at all): " + string.Join(", ", notAnApps));
    }

    [Fact]
    public void EveryRegisterFileOfThePlatform_IsInTheStarter()
    {
        // The register is the starter's: a register file only the platform has would be held in no app. (The root files and
        // the host are straight copies StarterTests holds equal; the test host and the suites differ in their usings.)
        var root = RepoRoot();
        var rules = ParseRules(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        var keep = ParseKeep(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        var embedded = RegisterFiles();
        var platformOnly = Tracked(root, ".").Where(p => Classify(p, rules) == Class.Register)
            .Where(p => !embedded.ContainsKey(StarterTests.ToStarter(p, keep))).ToList();
        Assert.True(platformOnly.Count == 0, "register-class files the starter does not carry: " + string.Join(", ", platformOnly));
    }

    [Fact]
    public void ThePlaceholder_IsRenamed_AsDotnetNewRenamesIt()
    {
        var renamed = PlaceholderBrand(new Dictionary<string, string> { ["Perezosoft"] = "Acme", ["perezosoft"] = "acme" });
        Assert.Equal(new Dictionary<string, string> { ["AppBrand"] = "Acme", ["APPBRAND"] = "ACME", ["appbrand"] = "acme" }, renamed);
        Assert.Equal("tests/Acme.Api.Tests/Perezosoft.Platform.Testing.props",
            LocalPath("tests/AppBrand.Api.Tests/Perezosoft.Platform.Testing.props", renamed, ["Perezosoft.Platform."]));
    }

    [Fact]
    public void EvaluateRegister_HoldsAnAppsCopies_ToThePackages()
    {
        var keep = new[] { "Perezosoft.Platform.", "key=\"perezosoft\"" };
        var brand = new Dictionary<string, string> { ["Perezosoft"] = "Acme", ["perezosoft"] = "acme" };
        var register = new Dictionary<string, byte[]>
        {
            ["build/A.props"] = Bytes("<!-- Perezosoft.Platform.Api from key=\"perezosoft\" -->\r\n<X>1</X>\n"),
            ["global.json"] = Bytes("{ \"sdk\": \"10.0.402\" }"),
            ["src/Host/Program.cs"] = Bytes("using Perezosoft.Infrastructure;\nnamespace AppBrand.Host; // appbrand APPBRAND\n"),
        };
        Verdict Run(Dictionary<string, string> app, Dictionary<string, string>? divergences = null) =>
            EvaluateRegister(register, brand, divergences ?? [], p => app.TryGetValue(p, out var t) ? Bytes(t) : null, keep);

        var inStep = new Dictionary<string, string>
        {
            ["build/A.props"] = "<!-- Perezosoft.Platform.Api from key=\"perezosoft\" -->\n<X>1</X>   \n", // line endings, trailing space
            ["global.json"] = "{ \"sdk\": \"10.0.402\" }",
            ["src/Host/Program.cs"] = "using Perezosoft.Infrastructure;\nnamespace Acme.Host; // acme ACME\n", // as dotnet new wrote it
        };
        Assert.Empty(Run(inStep).Failures);
        Assert.Equal(3, Run(inStep).RegisterFilesChecked);
        // The packages' namespaces are every app's: an app that renamed them has a copy that no longer builds on the packages.
        var rebranded = new Dictionary<string, string>(inStep) { ["src/Host/Program.cs"] = "using Acme.Infrastructure;\nnamespace Acme.Host; // acme ACME\n" };
        Assert.Contains(Run(rebranded).Failures, f => f.StartsWith("src/Host/Program.cs: differs", StringComparison.Ordinal));
        // A copy-only file (the template never renames it) compares equal as shipped.
        register["c.json"] = Bytes("{ \"keep\": [\"key=\\\"perezosoft\\\"\"] }"); // the map writes its keep token escaped
        Assert.Empty(Run(new Dictionary<string, string>(inStep) { ["c.json"] = "{ \"keep\": [\"key=\\\"perezosoft\\\"\"] }" }).Failures);
        register.Remove("c.json");

        // A brand that is also a word of the file: a faithful copy still compares equal (#421).
        var smoke = new Dictionary<string, string> { ["Perezosoft"] = "Smoke", ["perezosoft"] = "smoke" };
        var map = new Dictionary<string, byte[]> { ["m.json"] = Bytes("{ \"glob\": \"tests/native-smoke-android/**\" } // AppBrand") };
        Verdict RunSmoke(string text) => EvaluateRegister(map, smoke, new Dictionary<string, string>(), _ => Bytes(text), keep);
        Assert.Empty(RunSmoke("{ \"glob\": \"tests/native-smoke-android/**\" } // Smoke").Failures);
        Assert.NotEmpty(RunSmoke("{ \"glob\": \"tests/native-Smoke-android/**\" } // Smoke").Failures);

        var drifted = new Dictionary<string, string>(inStep) { ["global.json"] = "{ \"sdk\": \"10.0.300\" }" };
        Assert.Contains(Run(drifted).Failures, f => f.StartsWith("global.json: differs", StringComparison.Ordinal));
        Assert.Empty(Run(drifted, new() { ["global.json"] = "the app pins an older SDK until its host moves" }).Failures);
        Assert.Contains(Run(inStep, new() { ["global.json"] = "stale" }).Failures, f => f.Contains("identical to the package's again", StringComparison.Ordinal));
        Assert.Contains(Run(inStep, new() { ["nope.json"] = "a reason" }).Failures, f => f.StartsWith("nope.json:", StringComparison.Ordinal));

        var missing = new Dictionary<string, string>(inStep);
        missing.Remove("build/A.props");
        Assert.Contains(Run(missing).Failures, f => f.StartsWith("build/A.props: a register file", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStamp_SaysAnAppIsOnThePackages_Explicitly()
    {
        // Explicit, so a clone that lost its manifest still fails loudly instead of quietly being held to less.
        Assert.True(IsOnThePackages("""{ "platform": "argamboad/perezosoft-platform", "commit": "abc", "packages": true, "brand": {} }"""));
        Assert.False(IsOnThePackages("""{ "platform": "argamboad/perezosoft-platform", "commit": "abc", "brand": {} }"""));
        Assert.False(IsOnThePackages(File.ReadAllText(Path.Combine(RepoRoot(), "platform-stamp.json"))));
    }

    [Fact]
    public void AManifestWrittenBeforeTheNewClasses_ReadsAsWhatItMeantThen()
    {
        // A clone's manifest from before Pkg 6.4: "platform" was held, "adapts" was reported.
        var (_, entries) = ParseManifest("""{ "commit": "abc", "files": [ { "path": "a", "class": "platform", "hash": "1" }, { "path": "b", "class": "adapts", "hash": "2" }, { "path": "c", "class": "seed", "hash": "3" } ] }""");
        Assert.Equal([Class.Register, Class.Seed, Class.Seed], entries.Select(e => e.Class));
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    // Tracked files under a folder of the platform, relative to it, with forward slashes.
    private static IReadOnlyList<string> Tracked(string root, string folder)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-C", Path.Combine(root, folder), "ls-files" }) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git ls-files in {folder} failed");
        return o.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
