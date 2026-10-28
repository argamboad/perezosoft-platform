using Microsoft.AspNetCore.Mvc;
using Perezosoft.Api.Endpoints;
using Perezosoft.Shared.Ui.Capabilities;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 6.3 (#422): the seeded Home's list of what the platform gives an app (<see cref="PlatformCapabilities"/>) is the
/// API's. Every controller and every endpoint group appears exactly once, each route it shows is the platform's (and its
/// group's), each page is one the UI routes to, and each capability has its strings.
/// </summary>
public class PlatformCapabilitiesTests
{
    private static readonly IReadOnlyList<CapabilityEndpoint> Endpoints = PlatformCapabilities.All.SelectMany(c => c.Endpoints).ToList();

    // Every controller the platform ships, by name, with its route as routing compares it.
    private static readonly IReadOnlyDictionary<string, string> Controllers = typeof(PlatformHosting).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .ToDictionary(t => t.Name, t => RouteTableGuard.NormalizePrefix(
            t.GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>().Single().Template));

    // The platform's prefixes no controller holds: its minimal-API groups and its single endpoints (/api/version, /health).
    private static readonly IReadOnlyList<string> Groups = RouteTableGuard.PlatformPrefixes
        .Where(p => !Controllers.Values.Any(r => Under(r, p))).ToList();

    [Fact]
    public void EveryControllerAndEndpointGroup_AppearsOnce()
    {
        Assert.NotEmpty(Groups); // probe: the minimal-API groups are found
        var listed = Endpoints.Where(e => e.Group is not null).Select(e => e.Group!).ToList();
        var twice = listed.GroupBy(g => g).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var missing = Controllers.Keys.Concat(Groups).Except(listed).Order().ToList();
        var unknown = listed.Except(Controllers.Keys.Concat(Groups)).ToList();
        Assert.True(twice.Count == 0 && missing.Count == 0 && unknown.Count == 0,
            "PlatformCapabilities (src/Shared.Ui/Capabilities) out of step with the API — "
            + $"listed twice: [{string.Join(", ", twice)}]; missing: [{string.Join(", ", missing)}]; not the platform's: [{string.Join(", ", unknown)}]");
    }

    [Fact]
    public void EveryRoute_IsThePlatforms_AndItsGroups()
    {
        var off = Endpoints.Where(e =>
                !RouteTableGuard.PlatformPrefixes.Any(p => Under(e.Route, p))
                || e.Group is not null && !Under(e.Route, Controllers.TryGetValue(e.Group, out var route) ? route : e.Group))
            .Select(e => $"{e.Group ?? "(shown)"}: {e.Route}").ToList();
        Assert.True(off.Count == 0, "capability routes that are not the platform's, or not under their group: " + string.Join(", ", off));
    }

    [Fact]
    public void EveryPage_IsOneTheUiRoutesTo_AndEveryCapabilityHasItsStrings()
    {
        var routes = typeof(PlatformCapabilities).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false))
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>().Select(r => r.Template).ToHashSet();
        Assert.All(PlatformCapabilities.All.SelectMany(c => c.Pages), page => Assert.Contains(page, routes));

        // The English strings; ResourceParityTests holds the Spanish to them.
        var resx = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Shared.Ui", "Resources", "AppStrings.resx"));
        var unnamed = PlatformCapabilities.All.SelectMany(c => new[] { $"Capability_{c.Key}_Name", $"Capability_{c.Key}_What" })
            .Where(key => !resx.Contains($"name=\"{key}\"", StringComparison.Ordinal)).ToList();
        Assert.True(unnamed.Count == 0, "capability strings missing from AppStrings.resx: " + string.Join(", ", unnamed));
    }

    // A route at or below a prefix, as routing compares them.
    private static bool Under(string route, string prefix)
    {
        var r = RouteTableGuard.NormalizePrefix(route);
        var p = RouteTableGuard.NormalizePrefix(prefix);
        return r == p || r.StartsWith(p + "/", StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
