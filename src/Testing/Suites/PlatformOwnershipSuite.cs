using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Perezosoft.Testing.Architecture.PlatformOwnership;

namespace Perezosoft.Testing.Suites;

/// <summary>
/// Arch A2 (#363), R160, as an inheritable suite (Pkg 5.1b): the repo's checks against its ownership map, its stamp and
/// its manifest — every tracked file classified; downstream, every platform-class file equal to the stamped platform
/// commit's or listed in <c>PlatformDivergences.json</c> with a reason; the stamp and the manifest naming the same commit.
/// On the platform the stamp's commit is null and only the map check applies. An app on the packages (its stamp says
/// <c>"packages": true</c>, Pkg 5.7) keeps no manifest: the package it references is one, and its root files are held to the
/// register the package embeds (<see cref="RegisterFiles"/>). An app derives it once.
/// </summary>
public abstract class PlatformOwnershipSuite(ITestOutputHelper output)
{
    [Fact]
    public void OwnershipMap_ClassifiesEveryTrackedFile()
    {
        // A file nobody classified is a file nobody owns: its author decides at birth, or this fails the platform's own build.
        var root = RepoRoots.Find();
        var rules = ParseRules(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        Assert.True(rules.Count >= 40, $"probe: {rules.Count} rules parsed");
        // An app's own top-level folders (jigger-jot's seed/) are classed in its half of the map, after the platform's rules.
        var appMap = Path.Combine(root, "platform-ownership.App.json");
        if (File.Exists(appMap)) rules = [.. rules, .. ParseRules(File.ReadAllText(appMap))];
        var tracked = Git(root, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        // Probe: git answered for THIS tree. Not a count — an app on the packages carries a few dozen files (Pkg 5.1e).
        Assert.True(tracked.Contains("platform-ownership.json"), $"probe: git ls-files returned {tracked.Count} paths, without platform-ownership.json");

        var unclassified = tracked.Where(p => Classify(p, rules) is null).Order().ToList();
        Assert.True(unclassified.Count == 0, "tracked files with no ownership class — add a rule to platform-ownership.json (an app: platform-ownership.App.json): " + string.Join(", ", unclassified));

        // The decisions of the Architecture milestone, pinned: the A1 seams are the app's, the composition files the platform's,
        // the UI the app's (#368), the rules file the platform's.
        Assert.Equal(Class.App, Classify("src/Host/AppComposition.cs", rules));
        Assert.Equal(Class.App, Classify("src/Host/Persistence/AppDbContext.App.cs", rules));
        Assert.Equal(Class.App, Classify("tests/Api.Tests/App/AppAllowlists.cs", rules));
        Assert.Equal(Class.App, Classify("src/Host/Features/Budget/BudgetHandler.cs", rules));
        Assert.Equal(Class.Platform, Classify("src/Host/Program.cs", rules));
        Assert.Equal(Class.Platform, Classify("src/Infrastructure/Persistence/PlatformDbContext.cs", rules));
        Assert.Equal(Class.Platform, Classify("src/Host/Persistence/AppDbContext.cs", rules));
        // Pkg 3.9: the mails are the package's, branded through PlatformOptions.Brand — no longer a file an app edits.
        Assert.Equal(Class.Platform, Classify("src/Infrastructure/Email/BrandedEmail.cs", rules));
        Assert.Equal(Class.Platform, Classify("src/Infrastructure/Email/Assets/logo.png", rules));
        Assert.Equal(Class.Platform, Classify("src/Infrastructure/Email/EmailStrings.es.resx", rules));
        Assert.Equal(Class.Adapts, Classify("src/Host/Persistence/Migrations/20260617235537_InitialCreate.cs", rules));
        Assert.Equal(Class.Platform, Classify("tests/Api.Tests/ArchitectureTests.cs", rules));
        Assert.Equal(Class.Platform, Classify("docs/audits/v4-2026-09/FOUNDATION_RULES_v3.md", rules));
        Assert.Equal(Class.Adapts, Classify("src/Shared.Ui/Pages/Household.razor", rules));
        Assert.Equal(Class.Adapts, Classify("tests/E2E.Tests/SessionJourneyTests.cs", rules));
        Assert.Equal(Class.Adapts, Classify(".github/workflows/ci.yml", rules));
        Assert.Equal(Class.Sample, Classify("src/Host/Features/Notes/NotesHandler.cs", rules));
        // Pkg 2.5: the release machinery is the publisher's alone — never ported, never demanded downstream.
        foreach (var producer in new[] { ".github/workflows/release.yml", ".github/release-note-template.md", "tools/release/dry-run/DryRun.cs",
                                         "tests/ci-logic/cases/release-plan/dry-run/env", "platform-packages.props" })
            Assert.Equal(Class.Producer, Classify(producer, rules));
        // ...and the brand map never renames the platform's package ids or its feed key (ADR-034).
        var keep = ParseKeep(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        Assert.Contains("Perezosoft.Platform.", keep);
        Assert.Contains("key=\"perezosoft\"", keep);
    }

    [Fact]
    public void PlatformFiles_MatchTheStampedManifest_OrAreAllowlisted()
    {
        var root = RepoRoots.Find();
        var stamp = File.ReadAllText(Path.Combine(root, "platform-stamp.json"));
        var (stampCommit, brand) = ParseStamp(stamp);
        var manifestPath = Path.Combine(root, "tests", "Api.Tests", "App", "platform-manifest.json");
        var keep = ParseKeep(File.ReadAllText(Path.Combine(root, "platform-ownership.json")));
        if (IsOnThePackages(stamp))
        {
            // An app on the packages (Pkg 5.7): no port, so no manifest — its root files are held to the register the
            // platform package it references embeds, the platform's keep-list held as the package spells it.
            var register = RegisterFiles();
            Assert.True(register.Count >= 5, $"probe: the package embeds {register.Count} register files");
            var divergencesPath = Path.Combine(root, "tests", "Api.Tests", "App", "PlatformDivergences.json");
            var allowed = File.Exists(divergencesPath) ? ParseDivergences(File.ReadAllText(divergencesPath)) : new Dictionary<string, string>();
            var registerVerdict = EvaluateRegister(register, brand, allowed,
                local => File.Exists(Path.Combine(root, local)) ? File.ReadAllBytes(Path.Combine(root, local)) : null,
                ParseKeep(System.Text.Encoding.UTF8.GetString(register["platform-ownership.json"])));
            output.WriteLine($"on the packages: {registerVerdict.PlatformFilesChecked} register files checked against the package, {allowed.Count} allowlisted divergence(s)");
            Assert.True(registerVerdict.Failures.Count == 0, "root files out of step with the platform package this app references:\n  " + string.Join("\n  ", registerVerdict.Failures));
            return;
        }
        if (stampCommit is null)
        {
            // This IS the platform: nothing to compare against, and a manifest here would be a stale artifact.
            Assert.False(File.Exists(manifestPath), "the platform carries no platform-manifest.json; it is written downstream by the port");
            output.WriteLine("platform-stamp.json: commit null — this is the platform; the downstream gate does not apply.");
            return;
        }
        Assert.True(File.Exists(manifestPath), "platform-stamp.json names a platform commit but tests/Api.Tests/App/platform-manifest.json is missing — run tools/port-platform.ps1");
        var (manifestCommit, entries) = ParseManifest(File.ReadAllText(manifestPath));
        var divergences = ParseDivergences(File.ReadAllText(Path.Combine(root, "tests", "Api.Tests", "App", "PlatformDivergences.json")));

        var verdict = Evaluate(entries, manifestCommit, stampCommit, divergences, brand,
            local => File.Exists(Path.Combine(root, local)) ? File.ReadAllBytes(Path.Combine(root, local)) : null, keep);

        output.WriteLine($"platform {manifestCommit[..7]}: {verdict.PlatformFilesChecked} platform files checked, {divergences.Count} allowlisted divergence(s), {verdict.AdaptsDrift.Count} adapts file(s) differ:");
        foreach (var d in verdict.AdaptsDrift) output.WriteLine("  adapts: " + d);
        Assert.True(verdict.Failures.Count == 0, "platform files out of step with the stamped platform commit:\n  " + string.Join("\n  ", verdict.Failures));
    }

    [Fact]
    public void PlatformStamp_MatchesTheManifest()
    {
        var root = RepoRoots.Find();
        var stamp = File.ReadAllText(Path.Combine(root, "platform-stamp.json"));
        var (stampCommit, _) = ParseStamp(stamp);
        var manifestPath = Path.Combine(root, "tests", "Api.Tests", "App", "platform-manifest.json");
        if (IsOnThePackages(stamp))
        {
            // The package is the manifest (Pkg 5.7): a manifest file here would be a clone's leftover, held to nothing.
            Assert.False(File.Exists(manifestPath), "an app on the packages keeps no platform-manifest.json — the platform package it references is its manifest");
            return;
        }
        if (stampCommit is null) { Assert.False(File.Exists(manifestPath)); return; }
        Assert.True(File.Exists(manifestPath), "platform-stamp.json names a platform commit but tests/Api.Tests/App/platform-manifest.json is missing — run tools/port-platform.ps1");
        var (manifestCommit, _) = ParseManifest(File.ReadAllText(manifestPath));
        Assert.Equal(stampCommit, manifestCommit);
    }

    protected static string Git(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(repo);
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {e}");
        return o;
    }
}
