using Bunit;
using Xunit;
using AppBrand.Shared.Ui.Capabilities;
using AppBrand.Shared.Ui.Pages;
using AppBrand.Ui.Tests.Infrastructure;

namespace AppBrand.Ui.Tests;

/// <summary>
/// Proves the component-test chassis (v3 TOOL-2): renders the REAL <see cref="Home"/> page against the
/// doubles and asserts both auth branches. If this passes, the chassis correctly fakes localization + drives
/// AuthService signed-in/anonymous state — the foundation the client-UX slices (T39–T42) build on.
/// </summary>
public class HomePageTests : ComponentTestBase
{
    [Fact]
    public void Anonymous_ShowsHeroAndSignInCta()
    {
        var cut = Render<Home>();

        // The deterministic localizer renders keys, so we assert on the key the anonymous branch uses.
        Assert.Contains("Home_SignInCta", cut.Markup);
        Assert.Contains("/login", cut.Markup);
        Assert.DoesNotContain("Home_WelcomeBack", cut.Markup);
    }

    [Fact]
    public async Task SignedIn_ShowsWelcomeWithTheDisplayName()
    {
        await SignInAsync(name: "Ada Lovelace", tenantName: "Test Household");

        var cut = Render<Home>();

        // Home_WelcomeBack is a parameterized key; the fake localizer echoes the argument, so the display
        // name flowing from the JWT through AuthService into the component is observable.
        Assert.Contains("Home_WelcomeBack[Ada Lovelace]", cut.Markup);
        Assert.Contains("Home_SignedInTo[Test Household]", cut.Markup);
        Assert.DoesNotContain("Home_SignInCta", cut.Markup);
    }

    [Fact]
    public void Anonymous_DoesNotListTheCapabilities()
    {
        var cut = Render<Home>();

        Assert.Empty(cut.FindAll("[data-testid=capabilities]"));
    }

    [Fact]
    public async Task SignedIn_ListsEveryCapability_WithItsPagesAndEndpoints()
    {
        // Pkg 6.3: what the platform gives the app, one row per capability (PlatformCapabilities; the platform's
        // PlatformCapabilitiesTests holds the list to the API).
        await SignInAsync();

        var cut = Render<Home>();

        var rows = cut.FindAll("[data-testid=capabilities] tbody tr");
        Assert.Equal(PlatformCapabilities.All.Count, rows.Count);
        foreach (var capability in PlatformCapabilities.All)
        {
            var row = cut.Find($"[data-capability={capability.Key}]");
            Assert.Contains($"Capability_{capability.Key}_Name", row.TextContent);
            Assert.Contains($"Capability_{capability.Key}_What", row.TextContent);
            Assert.Equal(capability.Pages, row.QuerySelectorAll("a").Select(a => a.GetAttribute("href")!));
            Assert.Equal(capability.Endpoints.Select(e => e.Route), row.QuerySelectorAll("code").Select(c => c.TextContent));
            Assert.Equal(capability.Pages.Count == 0, row.TextContent.Contains("Home_CapabilityApiOnly", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SignedIn_LinksTheApisOwnDocuments_OnTheApiOrigin()
    {
        // /api/features and Swagger live on the API, not the web host: the links take the API client's base address.
        await SignInAsync();

        var cut = Render<Home>();

        Assert.Equal("http://localhost/api/features", cut.Find("[data-testid=capabilities-features]").GetAttribute("href"));
        Assert.Equal("http://localhost/swagger", cut.Find("[data-testid=capabilities-swagger]").GetAttribute("href"));
        Assert.Contains("Home_CapabilitiesPostman", cut.Find("[data-testid=capabilities]").TextContent);
    }

    [Fact]
    public async Task SignInHelper_DrivesTheRealRefreshEndpoint()
    {
        // Guard the chassis's own contract: SignInAsync must reach signed-in state THROUGH the refresh
        // endpoint (AuthService's real code path), not a reflection shortcut.
        await SignInAsync();

        Assert.True(Auth.IsAuthenticated);
        Assert.Contains(Http.Requests, r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/auth/refresh");
    }
}
