using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Endpoints;
using Perezosoft.Api.Tests.Infrastructure;

namespace Perezosoft.Api.Tests.Hosting;

/// <summary>
/// Pkg 3.10 (ADR-034 §4): an app claims its route prefixes by registering its slices' groups through
/// <c>MapTenantFeatureGroup(prefix)</c>, and <see cref="RouteTableGuard"/> takes them from there at boot. Once the platform
/// is a package an app repo has no platform controller source for the source-scanning <c>RouteGroupPrefixes_AreUnique</c>
/// gate to read, so the boot guard is what keeps two slices off one prefix and every slice off the platform's surfaces —
/// including a surface whose routes a gate left unmapped (<see cref="RouteTableGuard.PlatformPrefixes"/>).
/// </summary>
public class AppRoutePrefixTests
{
    private static WebApplication NewApp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        return builder.Build();
    }

    private static IEnumerable<Endpoint> EndpointsOf(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints);

    [Fact]
    public void AScratchAppsPrefix_IsRegistered_AndBoots()
    {
        var app = NewApp();
        app.MapTenantFeatureGroup("/api/projects").MapGet("/", () => "list");
        app.MapTenantFeatureGroup("/api/projects-archive").MapGet("/", () => "archive"); // a longer name is not a nested path

        RouteTableGuard.EnsureUnique(EndpointsOf(app)); // no throw

        // The registration is the group's metadata: every endpoint of it names the prefix it claimed.
        Assert.Single(EndpointsOf(app), e => e.Metadata.GetMetadata<AppRouteGroup>()?.Prefix == "/api/projects");
    }

    [Fact]
    public void TwoSlices_OnOnePrefix_FailEvenWhenTheirRoutesDiffer()
    {
        // The exact-route check cannot see this: GET /api/projects and GET /api/projects/{id} are different routes. Two
        // slices under one prefix still own each other's future routes.
        var app = NewApp();
        app.MapTenantFeatureGroup("/api/projects").MapGet("/", () => "a").WithDisplayName("Projects.List");
        app.MapTenantFeatureGroup("api/Projects/").MapGet("/{id}", (string id) => id).WithDisplayName("Boards.Get");

        var ex = Assert.Throws<InvalidOperationException>(() => RouteTableGuard.EnsureUnique(EndpointsOf(app)));
        Assert.Contains("/api/projects", ex.Message, StringComparison.Ordinal);
        Assert.Contains("two feature groups", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/household")]          // a platform surface itself
    [InlineData("/api/household/projects")] // nested inside one
    [InlineData("/api")]                    // covering all of them
    [InlineData("/api/billing")]            // a surface whose routes are unmapped while its gate is off
    public void AnAppPrefix_OnAPlatformSurface_FailsAtBoot(string prefix)
    {
        var app = NewApp();
        app.MapTenantFeatureGroup(prefix).MapGet("/mine", () => "x");

        var ex = Assert.Throws<InvalidOperationException>(() => RouteTableGuard.EnsureUnique(EndpointsOf(app)));
        Assert.Contains("platform", ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>Pkg 3.10: the reserved platform surfaces cover the platform's real route table with every gate on.</summary>
[Collection(IntegrationCollection.Name)]
public class PlatformPrefixCoverageTests(IntegrationTestFactory factory)
{
    [Fact]
    public void PlatformPrefixes_CoverEveryPlatformRoute_WithEveryGateOn()
    {
        // The reserved list is what keeps a gated-off surface reserved; it must cover every route the platform maps when
        // every gate is on, or an app could claim a surface the platform will use the day a gate opens.
        using var allOn = factory.WithGates([.. IntegrationTestFactory.AllGates()]);
        var platformRoutes = allOn.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<AppRouteGroup>() is null)
            .Select(e => "/" + (e.RoutePattern.RawText ?? "").Trim('/').ToLowerInvariant())
            .Where(p => !p.StartsWith("/{", StringComparison.Ordinal)) // the SPA fallback is a catch-all, not a surface
            .Distinct().ToList();
        Assert.True(platformRoutes.Count > 40, $"probe: only {platformRoutes.Count} platform routes");

        var uncovered = platformRoutes.Where(r => !RouteTableGuard.PlatformPrefixes.Any(p => r == p || r.StartsWith(p + "/", StringComparison.Ordinal))).Order().ToList();
        Assert.True(uncovered.Count == 0, "platform routes outside RouteTableGuard.PlatformPrefixes — add their surface: " + string.Join(", ", uncovered));
    }
}
