using System.Text.RegularExpressions;

namespace Perezosoft.Api.Tests;

/// <summary>
/// ADR-031: the shape of the one CI workflow. The repo is private, so every Actions minute is billed and the
/// maintainer manages them by hand: a pull request runs the web gates only, a merge runs nothing, and the device
/// legs and the deploys run only from "Run workflow". These facts hold that shape, so a job that quietly starts
/// running on every PR again (or a deploy that fires on a push) fails here, naming it. The Forgejo-era parity
/// checks that also guarded this file (R98, R137, R139, Env L29/L30) live here too since `.forgejo/` was removed.
/// Since Pkg 5.4 the shape has two halves: ci.yml keeps the triggers and the concurrency, and calls app-ci.yml, the
/// reusable workflow with every job — the one every app calls too.
/// </summary>
public class CiWorkflowTests
{
    private const string Ci = ".github/workflows/ci.yml";
    private const string AppCi = ".github/workflows/app-ci.yml";

    private static readonly string[] WebGates = ["build-test", "license-scan", "docker-build", "e2e"];
    private static readonly string[] DocsGates = ["secret-scan", "qa-artifacts"];
    private static readonly string[] DeviceLegs =
        ["native-build", "native-release-android", "native-build-apple", "native-smoke-apple", "native-smoke-windows", "native-smoke-android"];

    [Fact]
    public void Ci_RunsOnPullRequestsAndOnDemand_NeverOnAPushOrASchedule() // ADR-031
    {
        var header = Read(Ci).Split("\njobs:\n")[0];
        var on = header[header.IndexOf("\non:\n", StringComparison.Ordinal)..];
        Assert.Contains("  pull_request:\n    branches: [main, develop]", on, StringComparison.Ordinal);
        Assert.Contains("  workflow_dispatch:", on, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^  push:", on);
        Assert.DoesNotMatch(@"(?m)^  schedule:", on);

        // The two manual choices, with "none" as the default so a bare "Run workflow" does nothing costly.
        Assert.Contains("options: [none, staging, prod]", on, StringComparison.Ordinal);
        Assert.Contains("options: [none, android, windows, apple, all]", on, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(on, @"default: none").Count);
    }

    [Fact]
    public void EveryJob_GatesOnTheRunPlan_ForWhatItIs() // ADR-031
    {
        var jobs = Jobs(Read(AppCi));
        foreach (var job in WebGates)
            Assert.Contains("if: needs.changes.outputs.web == 'true'", jobs[job], StringComparison.Ordinal);
        foreach (var job in DocsGates)
            Assert.Contains("if: needs.changes.outputs.checks == 'true'", jobs[job], StringComparison.Ordinal);

        var deviceOutputs = new Regex(@"if: needs\.changes\.outputs\.(android|windows|apple) == 'true'|if: needs\.changes\.outputs\.buildmatrix != '\[\]'");
        foreach (var job in DeviceLegs)
            Assert.True(deviceOutputs.IsMatch(jobs[job]), $"{job} is a device leg: it must run only when the run plan asks for its platform");

        // Every job is accounted for: a new one has to be placed in one of the groups above.
        var known = WebGates.Concat(DocsGates).Concat(DeviceLegs).Concat(["changes", "deploy-staging", "deploy-prod"]).ToHashSet();
        var unplaced = jobs.Keys.Where(k => !known.Contains(k)).ToList();
        Assert.True(unplaced.Count == 0, "jobs in no group (web gate, docs gate, device leg, deploy): " + string.Join(", ", unplaced));
    }

    [Fact]
    public void RunPlan_ChecksEveryPr_WebOnCodeOrDeploy_DevicesOnlyOnDispatch() // ADR-031
    {
        var changes = Jobs(Read(AppCi))["changes"];
        var plan = changes[changes.IndexOf("# ci-logic begin: run-plan", StringComparison.Ordinal)..];
        // A pull request always gets the docs gates, and the web gates when code changed.
        Assert.Contains("if [ \"$EVENT\" = \"pull_request\" ]; then\n            checks=true; web=\"$CODE\"", plan, StringComparison.Ordinal);
        // A deploy runs every web gate whatever the diff.
        Assert.Contains("checks=true; web=true", plan, StringComparison.Ordinal);
        // The device legs are only ever chosen on a dispatch.
        Assert.Contains("if [ \"$EVENT\" = \"workflow_dispatch\" ]; then\n            case \"$DEVICES\" in", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploys_RunOnlyFromADispatch_BehindEveryWebGate_OnTheirOwnBranch() // ADR-031, ADR-017
    {
        var jobs = Jobs(Read(AppCi));
        foreach (var (job, target, branch) in new[] { ("deploy-staging", "staging", "develop"), ("deploy-prod", "prod", "main") })
        {
            var body = jobs[job];
            // Behind every web gate that ran: a gate the repo has no input for (no journeys, no Dockerfile) is skipped, not
            // failed, and a skipped need must not hold a deploy back (Pkg 5.4).
            Assert.Contains("if: ${{ !cancelled() && !contains(needs.*.result, 'failure') && github.event_name == 'workflow_dispatch' && inputs.deploy == '"
                + target + "' }}", body, StringComparison.Ordinal);
            Assert.Equal(WebGates.Concat(DocsGates).ToHashSet(), NeedsList(body));
            // The wrong branch fails loudly instead of deploying it.
            Assert.Contains($"if [ \"${{{{ github.ref }}}}\" != \"refs/heads/{branch}\" ]; then", body, StringComparison.Ordinal);
            Assert.DoesNotContain("github.event_name == 'push'", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryJob_HasATimeout() // ADR-031: GitHub's default is six hours, a month of minutes for one hung job
    {
        var missing = Jobs(Read(AppCi)).Where(j => !Regex.IsMatch(j.Value, @"(?m)^    timeout-minutes: \d+")).Select(j => j.Key).ToList();
        Assert.True(missing.Count == 0, "jobs without timeout-minutes: " + string.Join(", ", missing));
    }

    [Fact]
    public void ChangedFileLists_AreByteSafe() // v4 audit LB-DEP-2 (R137)
    {
        // By default git prints a path holding a byte >= 0x80 quoted and escaped: `src/Api/Features/Añadir.cs`
        // comes out as "src/Api/Features/AÃ±adir.cs". That no longer starts with `src/`, so the classifier
        // calls a code change code=false native=false docs=false and the run skips every gate. Turning
        // core.quotePath off makes git print the name as it is.
        var yml = Read(AppCi);
        Assert.Contains("git -c core.quotePath=false diff --name-only", yml, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"git diff --name-only", yml);
    }

    [Fact]
    public void EveryCheckout_LeavesNoTokenBehind() // v4 audit DEP-14 (H2), R98
    {
        // actions/checkout stores the job token in the workspace's git config unless told not to, where anything the
        // job runs afterwards (a NuGet restore without a lockfile, npm, brew) could push with it. So no checkout, in
        // any workflow, keeps it.
        var workflows = Directory.EnumerateFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(f => ".github/workflows/" + Path.GetFileName(f)).ToList();
        if (PlatformOwnership.IsThePlatform(RepoRoot())) // release.yml is producer-only (Pkg 2.5): an app has none
            Assert.Contains(".github/workflows/release.yml", workflows); // probe: the walk sees every workflow
        foreach (var file in workflows)
        {
            var lines = Read(file).Split('\n');
            var checkouts = lines.Select((line, i) => (line, i)).Where(x => x.line.Contains("uses: actions/checkout@", StringComparison.Ordinal)).ToList();
            if (checkouts.Count == 0) { Assert.Contains("uses: ./.github/workflows/", Read(file), StringComparison.Ordinal); continue; } // a caller only

            foreach (var (line, i) in checkouts)
            {
                // The step runs until the next line indented no deeper than its `- uses:`.
                var indent = line.IndexOf('-', StringComparison.Ordinal);
                var body = lines.Skip(i + 1).TakeWhile(l => l.Trim().Length == 0 || l.Length - l.TrimStart().Length > indent);
                Assert.True(body.Any(l => l.TrimStart().StartsWith("persist-credentials: false", StringComparison.Ordinal)),
                    $"{file}:{i + 1} checks out without `persist-credentials: false` — the job token would stay in .git/config.");
            }
        }

        // The one step that talks to the remote after checkout authenticates for that command only.
        var qa = Jobs(Read(AppCi))["qa-artifacts"];
        Assert.Contains("TOKEN: ${{ github.token }}", qa, StringComparison.Ordinal);
        Assert.Contains("http.extraheader=AUTHORIZATION: basic", qa, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeSmokeProviderProbe_MatchesTheStatusField_InBothSites() // v4 audit LB-DEP-4 (R139)
    {
        // The Windows and Apple smokes decide "the app booted" from a 200 on GET /api/auth/providers in api.log.
        // `.*200` matched a 200 anywhere later on the line, e.g. the elapsed time of a 404 ("200.1234ms"), or a
        // sibling route. Both patterns (bash + PowerShell) must read the status field.
        var patterns = Regex.Matches(Read(AppCi), @"(?:grep -cE|-Pattern) '(Request finished[^']*auth/providers[^']*)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, patterns.Count);

        const string real = "info: Microsoft.AspNetCore.Hosting.Diagnostics[2] Request finished HTTP/1.1 GET http://api.test/api/auth/providers - 200 - application/json;+charset=utf-8 14.2031ms";
        string[] impostors =
        [
            "info: Microsoft.AspNetCore.Hosting.Diagnostics[2] Request finished HTTP/1.1 GET http://api.test/api/auth/providers - 404 - application/problem+json 200.1234ms",
            "info: Microsoft.AspNetCore.Hosting.Diagnostics[2] Request finished HTTP/1.1 GET http://api.test/api/auth/providers - 500 0 - 205.0010ms",
            "info: Microsoft.AspNetCore.Hosting.Diagnostics[2] Request finished HTTP/1.1 GET http://api.test/api/auth/providers-beta - 200 - application/json 3.1ms",
            "info: Microsoft.AspNetCore.Hosting.Diagnostics[2] Request finished HTTP/1.1 POST http://api.test/api/auth/providers - 200 - application/json 3.1ms",
        ];
        foreach (var pattern in patterns)
        {
            Assert.Contains("providers - 200 ", pattern);
            Assert.Matches(pattern, real);
            foreach (var line in impostors)
                Assert.False(Regex.IsMatch(line, pattern), $"'{pattern}' matched a non-200 or sibling line: {line}");
        }
    }

    [Fact]
    public void E2eBrowser_ComesWithItsDeps_OnTheBareHostedRunner() // Env L24
    {
        // A hosted runner starts bare, so e2e installs Chromium's system packages with the browser; the journeys run
        // headless.
        Assert.Contains("playwright.ps1 install --with-deps chromium", Jobs(Read(AppCi))["e2e"], StringComparison.Ordinal);
        Assert.Contains("<Headless>true</Headless>", Read("tests/E2E.Tests/playwright.runsettings"), StringComparison.Ordinal);
    }

    [Fact]
    public void MauiClassifier_CoversWhatTheAppIsBuiltFrom_AndNothingElse() // Env L29
    {
        // src/Maui references only src/Shared.Ui, which references only src/Client (Pkg 4.1). Reported by the `changes` job for the
        // record (the device legs run on request since ADR-031), and kept exact so it can gate them again.
        var m = Regex.Match(Read(AppCi), @"maui=\$\(match ""\$nativefiles"" '([^']+)'\)");
        Assert.True(m.Success, "no `maui=` classifier");
        var maui = new Regex(m.Groups[1].Value);
        foreach (var path in new[] { "src/Maui/MauiProgram.cs", "src/Shared.Ui/Pages/Login.razor", "src/Client/Auth/AuthService.cs", "src/Client.Maui/PlatformMauiHosting.cs", "Directory.Build.props",
                                     "Directory.Packages.props", "global.json", ".github/workflows/ci.yml", ".github/workflows/app-ci.yml",
                                     ".config/dotnet-tools.json", ".github/forbidden-licenses.json" })
            Assert.True(maui.IsMatch(path), $"{path} can break the MAUI build, so it must count as maui");
        foreach (var path in new[] { "src/Host/Program.cs", "src/Core/Entities/Tenant.cs", "src/Infrastructure/Email/BrandedEmail.cs",
                                     "src/Web/Program.cs", "src/Client.Web/PlatformWebHosting.cs", "tests/Api.Tests/AuthTests.cs", "tests/E2E.Tests/Journeys.cs", "docs/notes/not-a-real-doc.md" })
            Assert.False(maui.IsMatch(path), $"{path} cannot affect the MAUI app (it references only Shared.Ui and, through it, Client)");
    }

    [Fact]
    public void Release_RunsOnlyOnDispatch_PacksWithMinVer_AndPublishesAfterTheChecks() // Pkg 2.3, ADR-034, ADR-031
    {
        // The packages leave this repo only from a manual "Run workflow": no push, no schedule (ADR-031), one job with a
        // timeout, never cancelled halfway (a release half-pushed is worse than a slow one). The version is a tag MinVer
        // reads, decided and checked by the release-plan block (its cases are in tests/ci-logic) before anything is packed;
        // the tag is pushed only after the packages are, so a failed push leaves no orphan tag.
        // MinVer gives every packable project its version from the tag; nothing is packable unless it says so. These props
        // travel to every app (an app packs nothing, so they are inert there).
        var props = Read("build/Packing.props"); // out of Directory.Build.props since Pkg 5.2
        Assert.Contains("<IsPackable Condition=\"'$(IsPackable)' == ''\">false</IsPackable>", props, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"MinVer\"", props, StringComparison.Ordinal);
        Assert.Contains("<MinVerTagPrefix>v</MinVerTagPrefix>", props, StringComparison.Ordinal);

        // The workflow and its note template are producer-only (Pkg 2.5): held here, in the platform; an app has neither.
        if (!PlatformOwnership.IsThePlatform(RepoRoot())) return;
        const string Release = ".github/workflows/release.yml";
        var yml = Read(Release);
        var on = yml[yml.IndexOf("\non:\n", StringComparison.Ordinal)..yml.IndexOf("\njobs:\n", StringComparison.Ordinal)];
        Assert.Contains("  workflow_dispatch:", on, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^  (push|pull_request|schedule):", on);
        Assert.Contains("cancel-in-progress: false", on, StringComparison.Ordinal);

        var job = Assert.Single(Jobs(yml)).Value;
        Assert.Matches(@"(?m)^    timeout-minutes: \d+", job);
        Assert.Contains("contents: write", job, StringComparison.Ordinal);
        Assert.Contains("packages: write", job, StringComparison.Ordinal);

        int At(string text) { var i = job.IndexOf(text, StringComparison.Ordinal); Assert.True(i >= 0, $"release.yml lost: {text}"); return i; }
        var plan = At("# ci-logic begin: release-plan");
        var pack = At("dotnet pack");
        var push = At("dotnet nuget push");
        var tag = At("push origin \"refs/tags/");
        Assert.True(plan < pack && pack < push && push < tag, "release.yml must plan, then pack, then push the packages, then push the tag");

        // What is packed (Pkg 3.11): the projects platform-packages.props maps, one restore and one pack each, decided by
        // the release-pack-list block (its cases in tests/ci-logic run against the real map). Never the solution: it holds
        // src/Maui (net10.0-android), which the ubuntu runner cannot restore without the MAUI workload — the first real
        // release would have died there, where the dry run (one sample project) never went.
        var list = At("# ci-logic begin: release-pack-list");
        Assert.True(plan < list && list < pack, "the pack list is decided after the plan and before anything is packed");
        Assert.Contains("platform-packages.props", job, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s+dotnet restore ""\$p"" --locked-mode -p:PlatformMauiAllTargets=true$", job);
        Assert.Matches(@"(?m)^\s+dotnet pack ""\$p"" -c Release --no-restore -p:PlatformMauiAllTargets=true -o artifacts$", job);
        Assert.DoesNotMatch(@"(?m)^\s+(pack=|dotnet (restore|pack) )[^#\r\n]*\.sln[xf]?\b", job); // no step names a solution

        // Where (Pkg 4.4): Perezosoft.Platform.Maui carries all four TFMs, and only a Windows host compiles
        // net10.0-windows — with the MAUI workloads it also compiles the library's iOS and Mac Catalyst targets. So the
        // one job runs on windows-latest, in bash (the ci-logic blocks are bash), installs the four workloads by name
        // (`workload restore` would read only this host's TFMs) at ci.yml's workload set, before anything is packed.
        Assert.Matches(@"(?m)^    runs-on: windows-latest$", job);
        Assert.Matches(@"(?m)^    defaults:\s+run:\s+shell: bash$", job);
        // The push expands the files in the shell, one package per push: Windows' dotnet does not expand a quoted
        // "artifacts/*.nupkg" (the alpha.2 run failed there), and --skip-duplicate lets a rerun finish a partial push.
        Assert.Matches(@"(?m)^\s+for p in artifacts/\*\.nupkg; do\s+dotnet nuget push ""\$p"" --source ""\$FEED"" --api-key ""\$TOKEN"" --skip-duplicate\s+done", job);
        Assert.DoesNotContain("dotnet nuget push \"artifacts/*.nupkg\"", job, StringComparison.Ordinal);
        var workloads = At("dotnet workload install maui-android maui-ios maui-maccatalyst maui-windows --version ");
        Assert.True(workloads < pack, "the MAUI workloads are installed before anything is packed");
        var workloadSet = Regex.Match(Read(AppCi), @"dotnet workload restore ""\$MAUI_PROJ"" --version (\S+)").Groups[1].Value;
        Assert.Contains($"--version {workloadSet}", job, StringComparison.Ordinal); // one workload set across the repo
        var maui = Read("src/Client.Maui/Perezosoft.Client.Maui.csproj");
        Assert.Contains("'$(PlatformMauiAllTargets)' == 'true'\">$(TargetFrameworks);net10.0-ios;net10.0-maccatalyst<", maui, StringComparison.Ordinal);

        Assert.Contains(".github/release-note-template.md", job, StringComparison.Ordinal);
        foreach (var section in new[] { "Schema change", "Register change", "Breaking" }) // ADR-034 §7
            Assert.Contains(section, Read(".github/release-note-template.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void ANewPushToAPullRequest_CancelsItsPreviousRun_AndNothingElse() // Env L30
    {
        // Grouped per pull request, so a superseded PR run stops billing. A dispatch groups by its own run id and is
        // never cancelled by another run.
        var header = Read(Ci).Split("\njobs:\n")[0];
        Assert.Contains("concurrency:\n  group: ${{ github.event_name == 'pull_request' && format('ci-pr-{0}', github.event.pull_request.number) || format('ci-run-{0}', github.run_id) }}\n  cancel-in-progress: true", header, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProcessCiStopsByName_IsTheRepoOwnRunningProject() // Pkg 3.2 slip, caught on PR #435; Pkg 5.4
    {
        // A step that stops a running app by name silently stops nothing when the name stops being the project that runs:
        // after the host split, `pkill -f 'Perezosoft.Api'` named a library, left the billing-on API on its port, and the
        // gates-off lane read the wrong server. Since one workflow serves every app (Pkg 5.4) no name is written down at
        // all: the host is the repo's src/Host project and the MAUI app its src/Maui project (app-names.sh), and they run.
        var root = RepoRoot();
        var app = Read(AppCi);
        Assert.Contains("pkill -f \"$(basename \"$HOST_PROJ\" .csproj)\"", app, StringComparison.Ordinal);
        Assert.Contains("Get-Process $env:MAUI_NAME", app, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"pkill -f '|Get-Process [A-Za-z]", app); // no literal process name left

        var names = Read(".github/scripts/app-names.sh");
        Assert.Contains("host=$(first 'src/Host/*.csproj')", names, StringComparison.Ordinal);
        Assert.Contains("maui=$(first 'src/Maui/*.csproj')", names, StringComparison.Ordinal);
        var host = Directory.EnumerateFiles(Path.Combine(root, "src", "Host"), "*.csproj").Single();
        Assert.Contains("Microsoft.NET.Sdk.Web", File.ReadAllText(host), StringComparison.Ordinal); // the host runs
    }

    [Fact]
    public void Ci_CallsTheReusableWorkflow_WithItsInputsAndSecrets() // Pkg 5.4
    {
        // The platform runs exactly what it ships: one job, the reusable workflow, the run's two choices passed through
        // ("none" for a pull request, which has neither), and the deploy hooks.
        var ci = Read(Ci);
        var job = Assert.Single(Jobs(ci)).Value;
        Assert.Contains("uses: ./.github/workflows/app-ci.yml", job, StringComparison.Ordinal);
        Assert.Contains("deploy: ${{ inputs.deploy || 'none' }}", job, StringComparison.Ordinal);
        Assert.Contains("devices: ${{ inputs.devices || 'none' }}", job, StringComparison.Ordinal);
        Assert.Contains("secrets: inherit", job, StringComparison.Ordinal);

        // app-ci.yml is called, never triggered: its only `on:` is workflow_call, with the two choices and the source switch.
        var app = Read(AppCi);
        var on = app[app.IndexOf("\non:\n", StringComparison.Ordinal)..app.IndexOf("\njobs:\n", StringComparison.Ordinal)];
        Assert.Matches(@"(?m)^  workflow_call:$", on);
        Assert.DoesNotMatch(@"(?m)^  (push|pull_request|workflow_dispatch|schedule):", on);
        foreach (var input in new[] { "deploy:", "devices:", "platform-source:" })
            Assert.Contains("      " + input, on, StringComparison.Ordinal);
        Assert.Contains("LOCKED: ${{ !inputs.platform-source && '--locked-mode' || '' }}", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFeed_IsReadWithTheJobToken_OnlyByTheJobsThatRestore() // Pkg 5.6 (#419)
    {
        // The dotnet tool (.config/dotnet-tools.json) and an app's Perezosoft.Platform.* packages come from GitHub
        // Packages, which asks for a token even for a public package. The job token reads them (packages: read, granted by
        // the caller too, since a called workflow can use no more), as NuGet's per-source variable — never written to a
        // file — and only on the jobs that restore: a job that runs a downloaded binary and restores nothing (the secret
        // scan) is not handed it.
        foreach (var file in new[] { Ci, AppCi })
        {
            Assert.Contains("permissions:\n  contents: read\n", Read(file), StringComparison.Ordinal);
            Assert.Matches(@"(?m)^  packages: read$", Read(file));
        }

        const string credential = "NuGetPackageSourceCredentials_perezosoft: Username=${{ github.actor }};Password=${{ github.token }}";
        var restores = new Regex(@"dotnet (restore|build|test|publish|tool restore|workload restore)\b");
        foreach (var (name, body) in Jobs(Read(AppCi)))
        {
            if (restores.IsMatch(body))
                Assert.True(body.Contains(credential, StringComparison.Ordinal), $"{name} restores but cannot read the platform's feed");
            else
                Assert.DoesNotContain("NuGetPackageSourceCredentials", body, StringComparison.Ordinal);
        }
        Assert.Contains("<add key=\"perezosoft\"", Read("nuget.config"), StringComparison.Ordinal); // the key the variable names
        Assert.DoesNotMatch(@"(?m)^env:\n(?:  .*\n)*  NuGetPackageSourceCredentials", Read(AppCi)); // never workflow-wide
    }

    [Fact]
    public void AppCi_NamesNoProjectNorAppByTheBrand_AndReadsThePlatformFilesFromThePlatform() // Pkg 5.4
    {
        // One workflow for every app: a project, an app id or the API-URL variable written with the platform's brand would
        // be right in the platform and wrong in every app. They come from app-names.sh. And a file the platform owns (a
        // script, the licence list, the QA checker) is read from the platform checkout, which an app's repo is not.
        var app = Read(AppCi);
        var branded = Regex.Matches(app, @".*(Perezosoft\.(Host|Web|Maui|E2E|Api|Core|Ui)|com\.perezosoft|PEREZOSOFT_).*").Select(m => m.Value.Trim()).ToList();
        Assert.True(branded.Count == 0, "app-ci.yml names the platform's brand:\n  " + string.Join("\n  ", branded));
        Assert.DoesNotMatch(@"bash \.github/scripts/", app);
        Assert.DoesNotMatch(@"--forbidden-license-types \.github/", app);
        Assert.Contains("repository: ${{ job.workflow_repository }}", app, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ job.workflow_sha }}", app, StringComparison.Ordinal);
        // Every job that reads $PLATFORM checks the platform out first (on the platform itself the step is skipped).
        foreach (var (name, body) in Jobs(app).Where(j => j.Value.Contains("$PLATFORM/", StringComparison.Ordinal)))
        {
            var checkout = body.IndexOf("path: .platform", StringComparison.Ordinal);
            Assert.True(checkout >= 0 && checkout < body.IndexOf("$PLATFORM/", StringComparison.Ordinal), $"{name} reads $PLATFORM without checking the platform out first");
        }
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative)).ReplaceLineEndings("\n");

    // Job blocks are the two-space keys under `jobs:`; each runs to the next such key.
    private static Dictionary<string, string> Jobs(string yml)
    {
        var lines = yml.Split('\n');
        var jobsAt = Array.FindIndex(lines, l => l.TrimEnd() == "jobs:");
        Assert.True(jobsAt >= 0, "no `jobs:` key");
        var starts = lines.Select((line, i) => (line, i))
            .Where(x => x.i > jobsAt && Regex.IsMatch(x.line, @"^  [a-z][a-z0-9-]*:\s*$"))
            .ToList();
        var jobs = new Dictionary<string, string>();
        for (var n = 0; n < starts.Count; n++)
        {
            var end = n + 1 < starts.Count ? starts[n + 1].i : lines.Length;
            jobs[starts[n].line.Trim().TrimEnd(':')] = string.Join("\n", lines[starts[n].i..end]);
        }
        return jobs;
    }

    // `needs: a` or `needs: [a, b,\n    c]` — the list may wrap onto following lines.
    private static HashSet<string> NeedsList(string job)
    {
        var m = Regex.Match(job, @"(?ms)^    needs:\s*(\[[^\]]*\]|\S+)");
        Assert.True(m.Success, "no `needs:` in job");
        return [.. Regex.Matches(m.Groups[1].Value, @"[a-z][a-z0-9-]*").Select(x => x.Value)];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
