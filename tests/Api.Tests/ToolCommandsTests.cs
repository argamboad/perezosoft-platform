using Perezosoft.Tool;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 5.3: <c>dotnet platform</c> runs the platform's scripts against the repo it is run in. Every operator script is a
/// command and every command's script ships beside the tool; the tool finds the repo, and a real command runs through it
/// end to end against this one.
/// </summary>
public class ToolCommandsTests
{
    [Fact]
    public void EveryOperatorScript_IsACommand_AndEveryCommandShipsItsScript()
    {
        var root = RepoRoot();
        var operatorScripts = Directory.EnumerateFiles(Path.Combine(root, "tools"), "*.ps1").Select(f => Path.GetFileName(f)!)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "gen_qa_*.py").Select(f => Path.GetFileName(f)!))
            .Append("check_qa_artifacts.py")
            .Order().ToList();
        Assert.True(operatorScripts.Count >= 10, "probe: tools/*.ps1 or docs/gen_qa_*.py moved");
        Assert.Equal(operatorScripts, ToolCommands.Scripts.Select(c => c.Script).Order().ToList());

        // The scripts sit beside the tool (a referenced project's content lands beside this assembly too).
        foreach (var c in ToolCommands.Scripts)
            Assert.True(File.Exists(Path.Combine(ToolCommands.ScriptsDirectory, c.Script)), $"{c.Name}: {c.Script} is not beside the tool");
        Assert.True(File.Exists(Path.Combine(ToolCommands.ScriptsDirectory, "requirements.txt")), "the QA commands' requirements ship with them");

        Assert.Equal(ToolCommands.Scripts.Count + 1, ToolCommands.Scripts.Select(c => c.Name).Append(ToolCommands.Up).Distinct().Count());
        foreach (var name in ToolCommands.Scripts.Select(c => c.Name).Append(ToolCommands.Up))
            Assert.Contains($"  {name} ", ToolCommands.Help());
    }

    [Fact]
    public void TheTool_FindsTheRepo_FromInsideIt_AndNothingOutsideOne()
    {
        var root = RepoRoot();
        Assert.Equal(root, ToolCommands.FindRepoRoot(Path.Combine(root, "src", "Tool")));
        Assert.Null(ToolCommands.FindRepoRoot(Path.GetPathRoot(root)!));
    }

    [Fact]
    public async Task ACommand_RunsItsScript_AgainstTheRepo() // needs pwsh 7, as every tools script does
    {
        // `ports` without -Apply is read-only: it compares every derived port site with local-ports.props and exits 0 in step.
        var exit = await ToolCommands.RunAsync(["ports"], Path.Combine(RepoRoot(), "src", "Host"));
        Assert.Equal(0, exit);
        Assert.Equal(2, await ToolCommands.RunAsync(["no-such-command"], RepoRoot()));
    }

    private static string RepoRoot() =>
        ToolCommands.FindRepoRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("repo root not found");
}
