using System.Text;
using static Perezosoft.Testing.Architecture.PlatformOwnership;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 5.7 (#450): an app on the packages keeps no port and no manifest; its root files — the register — are held to the
/// copies the platform package it references embeds. These hold the register here, in the platform: what it embeds, that
/// an app's copy can compare equal however the app was branded, and the verdict on fixtures.
/// </summary>
public class PlatformRegisterTests
{
    private static readonly string[] Register =
        ["build/PlatformSource.props", "build/PlatformSource.targets", "global.json", "nuget.config", ".config/dotnet-tools.json", "platform-ownership.json"];

    [Fact]
    public void TheRegister_IsEmbedded_AsThePlatformHasIt_AndEveryFileIsThePlatforms()
    {
        var root = RepoRoot();
        var rules = ParseRules(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        var embedded = RegisterFiles();
        Assert.Equal(Register.Order(), embedded.Keys.Order());
        foreach (var path in Register)
        {
            Assert.Equal(Normalize(File.ReadAllBytes(Path.Combine(root, path)), []), Normalize(embedded[path], []));
            Assert.Equal(Class.Platform, Classify(path, rules)); // an adapts file an app edits; the register holds what it must not
        }
    }

    [Fact]
    public void TheRegister_NamesTheBrand_OnlyInsideTheKeepList()
    {
        // A register file must mean the same in a rebranded app as in the packages: a name holding the brand outside a
        // keep-list token (a package id) is rewritten in the app and not in the package. The switch's import guards were
        // renamed for this (_PlatformBuildPropsImported): a rebranded app's switch looked for another name than the Build
        // package set, and imported its files twice. And the tool's id in .config/dotnet-tools.json is lower-case (#455):
        // until the keep-list held that spelling, an app's correct copy failed the register and a renamed one, naming a
        // tool that does not exist, passed it. The map itself is out: it spells its own keep tokens.
        var keep = ParseKeep(File.ReadAllText(Path.Combine(RepoRoot(), "platform-ownership.json")));
        foreach (var (path, bytes) in RegisterFiles().Where(f => f.Key != "platform-ownership.json"))
        {
            var text = NormalizeKeeping(bytes, ["Perezosoft", "perezosoft"], keep);
            Assert.False(text.Contains("{BRAND}", StringComparison.Ordinal) || text.Contains("{brand}", StringComparison.Ordinal),
                $"{path} names the brand outside the keep-list — an app's copy would differ by its brand alone");
        }
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
        };
        Verdict Run(Dictionary<string, string> app, Dictionary<string, string>? divergences = null) =>
            EvaluateRegister(register, brand, divergences ?? [], p => app.TryGetValue(p, out var t) ? Bytes(t) : null, keep);

        var inStep = new Dictionary<string, string>
        {
            ["build/A.props"] = "<!-- Perezosoft.Platform.Api from key=\"perezosoft\" -->\n<X>1</X>   \n", // line endings, trailing space
            ["global.json"] = "{ \"sdk\": \"10.0.402\" }",
        };
        Assert.Empty(Run(inStep).Failures);
        Assert.Equal(2, Run(inStep).PlatformFilesChecked);
        // Rebranded outside the keep-list, or copied as shipped: equal either way.
        var rebranded = new Dictionary<string, string>(inStep) { ["build/A.props"] = "<!-- Perezosoft.Platform.Api from key=\"perezosoft\" -->\n<X>1</X>" };
        Assert.Empty(Run(rebranded).Failures);
        register["c.json"] = Bytes("{ \"keep\": [\"key=\\\"perezosoft\\\"\"] }"); // the map writes its keep token escaped
        var copied = new Dictionary<string, string>(inStep) { ["c.json"] = "{ \"keep\": [\"key=\\\"perezosoft\\\"\"] }" };
        var branded = new Dictionary<string, string>(inStep) { ["c.json"] = "{ \"keep\": [\"key=\\\"acme\\\"\"] }" };
        Assert.Empty(Run(copied).Failures);
        Assert.Empty(Run(branded).Failures);
        register.Remove("c.json");

        // A brand that is also a word of the file: a faithful copy, as shipped or branded, still compares equal (#421).
        var smoke = new Dictionary<string, string> { ["Perezosoft"] = "Smoke", ["perezosoft"] = "smoke" };
        var map = new Dictionary<string, byte[]> { ["m.json"] = Bytes("{ \"glob\": \"tests/native-smoke-android/**\" } // Perezosoft") };
        Verdict RunSmoke(string text) => EvaluateRegister(map, smoke, new Dictionary<string, string>(), _ => Bytes(text), keep);
        Assert.Empty(RunSmoke("{ \"glob\": \"tests/native-smoke-android/**\" } // Perezosoft").Failures);
        Assert.Empty(RunSmoke("{ \"glob\": \"tests/native-smoke-android/**\" } // Smoke").Failures);
        Assert.NotEmpty(RunSmoke("{ \"glob\": \"tests/native-Smoke-android/**\" } // Smoke").Failures);

        var drifted = new Dictionary<string, string>(inStep) { ["global.json"] = "{ \"sdk\": \"10.0.300\" }" };
        Assert.Contains(Run(drifted).Failures, f => f.StartsWith("global.json: differs", StringComparison.Ordinal));
        Assert.Empty(Run(drifted, new() { ["global.json"] = "the app pins an older SDK until its host moves" }).Failures);
        Assert.Contains(Run(inStep, new() { ["global.json"] = "stale" }).Failures, f => f.Contains("identical to the package's again", StringComparison.Ordinal));
        Assert.Contains(Run(inStep, new() { ["nope.json"] = "a reason" }).Failures, f => f.StartsWith("nope.json:", StringComparison.Ordinal));

        var missing = new Dictionary<string, string>(inStep);
        missing.Remove("build/A.props");
        Assert.Contains(Run(missing).Failures, f => f.StartsWith("build/A.props: a platform file", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStamp_SaysAnAppIsOnThePackages_Explicitly()
    {
        // Explicit, so a clone that lost its manifest still fails loudly instead of quietly being held to less.
        Assert.True(IsOnThePackages("""{ "platform": "argamboad/perezosoft-platform", "commit": "abc", "packages": true, "brand": {} }"""));
        Assert.False(IsOnThePackages("""{ "platform": "argamboad/perezosoft-platform", "commit": "abc", "brand": {} }"""));
        Assert.False(IsOnThePackages(File.ReadAllText(Path.Combine(RepoRoot(), "platform-stamp.json"))));
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
