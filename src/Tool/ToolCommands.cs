using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Perezosoft.Tool;

/// <summary>
/// The commands of <c>dotnet platform</c> (Pkg 5.3): each runs one of the platform's scripts, shipped beside the tool in
/// <c>scripts/</c>, against the repo the tool is run in (the folder above holding platform-stamp.json and
/// Directory.Build.props), which the script reads from <c>PLATFORM_REPO_ROOT</c>. Arguments after the command pass
/// through untouched, and the script's exit code is the tool's. <c>up</c> is the one command of its own: the repo's
/// compose services and the machine's telemetry dashboard.
/// </summary>
public static class ToolCommands
{
    /// <summary>One command: its name, the interpreter, the script in scripts/, and one line for the help.</summary>
    public sealed record Command(string Name, Runner Runner, string Script, string Summary);

    /// <summary>What runs a script: PowerShell 7 from the repo root, or Python from the repo's docs/.</summary>
    public enum Runner { Pwsh, Python }

    /// <summary>Every script command, in the order the help lists them.</summary>
    public static IReadOnlyList<Command> Scripts { get; } =
    [
        new("port", Runner.Pwsh, "port-platform.ps1", "merge the platform's register into this repo from a release (-Platform <checkout> [-To <version>] [-From <ref>] [-Apply]); a clone: from a commit"),
        new("ports", Runner.Pwsh, "ports.ps1", "hold every file that states a local port to local-ports.props (reports; -Apply rewrites)"),
        new("e2e", Runner.Pwsh, "e2e.ps1", "run the browser journeys against a local stack ([-Filter <f>] [-Gates] [-NoBuild] [-KeepRunning])"),
        new("publish-native", Runner.Pwsh, "publish-native.ps1", "publish the native apps (-ApiBaseUrl <url> [-Android] [-Windows] [-KeyStore <path> -KeyAlias <alias>])"),
        new("android-emulator", Runner.Pwsh, "android-emulator.ps1", "start the Android emulator for the native app"),
        new("dev-profiles", Runner.Pwsh, "dev-profiles.ps1", "write the editor's launch profiles for this repo"),
        new("telemetry", Runner.Pwsh, "telemetry.ps1", "start (or reuse) the machine's shared Aspire dashboard"),
        new("qa-guide", Runner.Python, "gen_qa_guide.py", "regenerate docs/QA_TEST_GUIDE.pdf from docs/QA_TEST_PLAN.md"),
        new("qa-runlog", Runner.Python, "gen_qa_runlog.py", "regenerate docs/QA_RUN_LOG.pdf from docs/QA_TEST_PLAN.md"),
        new("check-qa", Runner.Python, "check_qa_artifacts.py", "check the QA PDFs and the sign-off sheet are in step with docs/QA_TEST_PLAN.md"),
    ];

    /// <summary>The command that starts the local stack: the repo's db and mail services, and the telemetry dashboard.</summary>
    public const string Up = "up";

    /// <summary>The compose services <see cref="Up"/> starts (the app container is the image's own concern).</summary>
    public static IReadOnlyList<string> UpServices { get; } = ["db", "mail"];

    /// <summary>Where the scripts sit: beside the tool's assembly.</summary>
    public static string ScriptsDirectory => Path.Combine(AppContext.BaseDirectory, "scripts");

    public static async Task<int> RunAsync(string[] args, string workingDirectory)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "-?")
        {
            Console.WriteLine(Help());
            return args.Length == 0 ? 1 : 0;
        }
        if (args[0] is "--version")
        {
            Console.WriteLine(typeof(ToolCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return 0;
        }

        var root = FindRepoRoot(workingDirectory);
        if (root is null)
        {
            Console.Error.WriteLine("dotnet platform: run it inside a platform or app repo (a folder above holds platform-stamp.json and Directory.Build.props).");
            return 2;
        }

        if (args[0] == Up) return await UpAsync(root, args[1..]);

        var command = Scripts.FirstOrDefault(c => c.Name == args[0]);
        if (command is null)
        {
            Console.Error.WriteLine($"dotnet platform: no command '{args[0]}'.\n\n{Help()}");
            return 2;
        }
        return await RunScriptAsync(command, root, args[1..]);
    }

    /// <summary>The repo the tool works on: the nearest folder at or above <paramref name="start"/> with both root files.</summary>
    public static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "platform-stamp.json")) && File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
        return null;
    }

    public static string Help()
    {
        var width = Scripts.Select(c => c.Name.Length).Append(Up.Length).Max() + 2;
        var lines = Scripts.Select(c => $"  {c.Name.PadRight(width)}{c.Summary}")
            .Append($"  {Up.PadRight(width)}start the local stack: compose {string.Join(" + ", UpServices)}, and the telemetry dashboard");
        return "usage: dotnet platform <command> [arguments]\n\n" + string.Join("\n", lines)
            + "\n\nThe scripts run against the repo you are in; arguments after the command go to the script.";
    }

    private static async Task<int> RunScriptAsync(Command command, string root, string[] rest)
    {
        var script = Path.Combine(ScriptsDirectory, command.Script);
        if (!File.Exists(script))
        {
            Console.Error.WriteLine($"dotnet platform: {command.Script} is missing from {ScriptsDirectory} — reinstall the tool.");
            return 2;
        }
        if (command.Runner == Runner.Pwsh)
            return await StartAsync(["pwsh"], ["-NoProfile", "-File", script, .. rest], root, root,
                "PowerShell 7 (pwsh) is not on PATH: install it (https://aka.ms/powershell).");

        var exit = await StartAsync(["python", "py"], [script, .. rest], Path.Combine(root, "docs"), root,
            "Python is not on PATH: install Python 3.12 and its packages for the QA commands: "
            + $"python -m pip install -r \"{Path.Combine(ScriptsDirectory, "requirements.txt")}\"");
        if (exit != 0)
            Console.Error.WriteLine($"(the QA commands need Python's packages: python -m pip install -r \"{Path.Combine(ScriptsDirectory, "requirements.txt")}\")");
        return exit;
    }

    private static async Task<int> UpAsync(string root, string[] rest)
    {
        if (rest.Length > 0)
        {
            Console.Error.WriteLine("dotnet platform up: takes no arguments.");
            return 2;
        }
        var compose = Path.Combine(root, "docker-compose.yml");
        var exit = await StartAsync(["docker"], ["compose", "-f", compose, "up", "-d", "--wait", .. UpServices], root, root,
            "Docker is not on PATH: start Docker Desktop (or install Docker).");
        if (exit != 0) return exit;
        return await RunScriptAsync(Scripts.Single(c => c.Script == "telemetry.ps1"), root, []);
    }

    /// <summary>Runs the first of <paramref name="candidates"/> that exists, in the foreground, and returns its exit code.</summary>
    private static async Task<int> StartAsync(string[] candidates, string[] arguments, string workingDirectory, string root, string missing)
    {
        foreach (var file in candidates)
        {
            var start = new ProcessStartInfo(file) { WorkingDirectory = workingDirectory, UseShellExecute = false };
            foreach (var a in arguments) start.ArgumentList.Add(a);
            start.Environment["PLATFORM_REPO_ROOT"] = root;
            try
            {
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            catch (Win32Exception)
            {
                // not on PATH: try the next candidate
            }
        }
        Console.Error.WriteLine("dotnet platform: " + missing);
        return 127;
    }
}
