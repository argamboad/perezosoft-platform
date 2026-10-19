using System.Net;
using System.Text.Json;
using Perezosoft.Infrastructure.Persistence;
using Xunit;

namespace Perezosoft.Testing.Suites;

/// <summary>
/// The composition smoke (Pkg 5.1b): boots the app's REAL host — its Program, its AppComposition, its migrations, the
/// platform registered by AddPlatform — against the throwaway database, and asks the three questions a broken
/// composition fails first: does it start and report healthy, does it say which platform it is built on, and does an
/// authorized tenant call go all the way through (bearer → tenant filter → controller → EF → Postgres, under RLS). An
/// app derives it once over its integration host, in the integration collection.
/// </summary>
public abstract class PlatformCompositionSmoke<TProgram, TContext>(PlatformIntegrationFactory<TProgram, TContext> factory)
    where TProgram : class
    where TContext : PlatformDbContext
{
    [Fact]
    public async Task TheHost_Starts_AndReportsReady()
    {
        // Ready, not merely alive: the readiness probe checks the database the startup migrated.
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheVersion_NamesThePlatformItIsBuiltOn()
    {
        var response = await factory.CreateClient().GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("platform", out _), "/api/version no longer reports the platform commit");
    }

    [Fact]
    public async Task ASignedInOwner_LoadsTheirHousehold()
    {
        var owner = await factory.SeedUserAsync();
        var response = await factory.CreateClientFor(owner).GetAsync("/api/household");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousCall_ToATenantRoute_IsRefused()
    {
        var response = await factory.CreateClient().GetAsync("/api/household");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
