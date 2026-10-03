using System.Text.RegularExpressions;

namespace Perezosoft.Api.Tests;

/// <summary>
/// v4 audit T59 (TB-DOC-5, ADV-P4-14 — R149, and the ban half of R154). Two contracts between the product and
/// the things that check it, both of which used to hold by habit:
/// <list type="bullet">
/// <item>The <b>test-id contract</b>: a <c>data-testid</c> in <c>src/Shared.Ui</c> is an interface — component
/// tests, browser journeys, the Android smoke and the QA plan all address the UI through it, and the planned
/// stack-neutral spec (FLAVORS SPEC-4) is built on the same list. An id nothing references can be renamed
/// without a failure; a referenced id that no longer exists is a test that cannot find its control.</item>
/// <item><b>No process-wide switches in tests</b>: a <c>[ModuleInitializer]</c> that sets an environment
/// variable changes every test in the run. A test that needs a config gate on asks
/// <c>IntegrationTestFactory.WithGates</c> for a host of its own.</item>
/// </list>
/// </summary>
public class TestIdContractTests
{
    [Fact]
    public void EveryTestId_IsUsedByATestOrAQaCase_AndEveryUsedIdExists()
    {
        var root = RepoRoot();
        var declared = SourceFiles(Path.Combine(root, "src", "Shared.Ui"), "*.razor")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"data-testid=""([^""]*)""").Select(m => (File: Path.GetFileName(f), Id: m.Groups[1].Value)))
            .ToList();
        Assert.True(declared.Count >= 90, $"probe: only {declared.Count} data-testid attributes found under src/Shared.Ui");

        // An id is a literal. A computed one (data-testid="row-@id") cannot be listed, searched for or put in a spec.
        var computed = declared.Where(d => d.Id.Contains('@')).Select(d => $"{d.File}: {d.Id}").ToList();
        Assert.True(computed.Count == 0, "computed data-testid values (use a literal id and a data- attribute for the variable part): " + string.Join(", ", computed));
        var ids = declared.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        // Where the UI is addressed from: C# tests (bUnit selectors, Playwright page objects), the Node smoke
        // and JS tests, and the QA plan.
        var consumers = SourceFiles(Path.Combine(root, "tests"), "*.cs")
            .Concat(SourceFiles(Path.Combine(root, "tests"), "*.js").Where(f => !f.Contains("node_modules")))
            .Where(f => Path.GetFileName(f) != "TestIdContractTests.cs") // this file's own examples are not references
            .Append(Path.Combine(root, "docs", "QA_TEST_PLAN.md"))
            .Select(File.ReadAllText).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in consumers)
        {
            foreach (Match m in Regex.Matches(text, @"data-testid=\\?['""]?([A-Za-z0-9_-]+)")) used.Add(m.Groups[1].Value);      // [data-testid=x] / ='x' / =\"x\"
            foreach (Match m in Regex.Matches(text, @"(?i:GetByTestId|TestId)\(\s*\$?['""]([A-Za-z0-9_-]+)['""]")) used.Add(m.Groups[1].Value); // GetByTestId("x") / getByTestId('x')
        }
        Assert.True(used.Count >= 80, $"probe: only {used.Count} test-id references found under tests/ and the QA plan");

        var unused = ids.Except(used).Order().ToList();
        Assert.True(unused.Count == 0, "data-testid values no test or QA case references (drive the control from a test, or drop the id): " + string.Join(", ", unused));
        var missing = used.Except(ids).Order().ToList();
        Assert.True(missing.Count == 0, "test ids referenced by a test or the QA plan that no longer exist in src/Shared.Ui: " + string.Join(", ", missing));
    }

    [Fact]
    public void Tests_DoNotSwitchTheEnvironment_FromAModuleInitializer()
    {
        // One exists, and it is not a feature switch: it opts the whole test assembly out of loading the
        // developer's local .env, so a personal SMTP password cannot leak into a test run. Named here with that
        // reason; any other [ModuleInitializer] in tests/ is refused — use IntegrationTestFactory.WithGates.
        var allowed = new Dictionary<string, string>
        {
            ["tests/Api.Tests/LocalDotEnvTests.cs"] = "opts the assembly out of the developer's .env (LocalDotEnv.SkipVariable); not a feature gate",
        };

        var root = RepoRoot();
        var found = SourceFiles(Path.Combine(root, "tests"), "*.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^\s*\[(?:System\.Runtime\.CompilerServices\.)?ModuleInitializer\]", RegexOptions.Multiline))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(allowed.Keys.Order(), found.Order());
    }

    private static IEnumerable<string> SourceFiles(string dir, string pattern) =>
        Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
