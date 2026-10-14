using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Configuration;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core;
using Perezosoft.Core.Billing;

namespace Perezosoft.Api.Tests.Billing;

/// <summary>
/// The plan catalog is code/config (ADR-006), not tenant data, so its shape is pinned here rather than
/// discovered at runtime. Two properties matter beyond the example numbers: Free is the fail-closed
/// landing place for anything unrecognised (which is what makes GATES-1's "billing off ⇒ everyone Free"
/// need no economics code of its own), and the Free seat limit is the cap a whole private test lives
/// inside. Since Pkg 3.7 (ADR-034 §4) the plans are a host option: <c>PlatformOptions.Plans</c> carries the platform's
/// defaults, an app replaces them in <c>AddPlatform(o => ...)</c>, and <see cref="PlanCatalog"/> serves what it was given.
/// </summary>
public class PlanCatalogTests
{
    private static PlanCatalog Catalog => TestPlans.Default;

    [Fact]
    public void Free_SeatLimit_LeavesHeadroomForAFamilyPlusOne()
    {
        // GATES-1/ADR-027 raised this 3 → 5. Three was exactly one household (the maintainer's is three
        // people) with zero room, and a merely PENDING invitation already consumes a seat — so the
        // fourth person could not be invited at all. Downstream apps re-tune it; the platform ships
        // room to actually run a friends test.
        Assert.Equal(5, Catalog.Get(PlanKeys.Free).SeatLimit);
    }

    [Fact]
    public void UnknownPlanKey_FailsClosedToFree()
    {
        // Absent/garbage plan key ⇒ Free, never the paid tier. GATES-1 leans on this: with billing off
        // no tenant has a subscription projection at all, so every tenant resolves here.
        var resolved = Catalog.Get("no-such-plan");

        Assert.Equal(PlanKeys.Free, resolved.Key);
        Assert.False(resolved.Includes(Entitlements.ProFeature));
    }

    [Fact]
    public void Free_GrantsNoPaidEntitlement()
    {
        // The accepted consequence of running with billing off: nobody holds the paid entitlement.
        Assert.False(Catalog.Get(PlanKeys.Free).Includes(Entitlements.ProFeature));
        Assert.True(Catalog.Get(PlanKeys.Pro).Includes(Entitlements.ProFeature));
    }

    // ── Pkg 3.7: the catalog is the host's to configure; the platform's numbers are the default ──

    [Fact]
    public void TheDefaultPlans_AreTodaysCatalog()
    {
        // Behaviour unchanged by the seam: Free 5 seats / 3 exports, Pro 10 seats / 100 exports, one paid entitlement.
        Assert.Equal([PlanKeys.Free, PlanKeys.Pro], new PlatformOptions().Plans.Select(p => p.Key));
        var free = Catalog.Get(PlanKeys.Free);
        var pro = Catalog.Get(PlanKeys.Pro);
        Assert.Equal((5, 3), (free.SeatLimit, free.UsageLimit(UsageKeys.Export)));
        Assert.Equal((10, 100), (pro.SeatLimit, pro.UsageLimit(UsageKeys.Export)));
        Assert.Empty(free.Entitlements);
        Assert.Equal([Entitlements.ProFeature], pro.Entitlements);
    }

    [Fact]
    public void AnAppThatReplacesThePlans_GetsItsOwnCatalog()
    {
        var solo = new Plan(PlanKeys.Free, new HashSet<string>(), SeatLimit: 1);
        var team = new Plan("team", new HashSet<string> { "team-feature" }, SeatLimit: 25);

        var services = new ServiceCollection();
        services.AddPlatformOptions(o => { o.Plans.Clear(); o.Plans.Add(solo); o.Plans.Add(team); });
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<PlanCatalog>();

        Assert.Same(team, catalog.Get("team"));
        Assert.Equal(1, catalog.Get(PlanKeys.Free).SeatLimit);
        Assert.Equal(PlanKeys.Free, catalog.Get(PlanKeys.Pro).Key); // the platform's Pro is gone: it fails closed to Free
        Assert.Same(catalog, provider.GetRequiredService<PlanCatalog>()); // one catalog per host
    }

    [Fact]
    public void WithoutAConfigureCall_TheHostGetsTheDefaults()
    {
        var services = new ServiceCollection();
        services.AddPlatformOptions();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(5, provider.GetRequiredService<PlanCatalog>().Get(PlanKeys.Free).SeatLimit);
        Assert.Equal(2, provider.GetRequiredService<PlatformOptions>().Plans.Count);
    }

    [Fact]
    public void PlansWithoutFree_AreRefusedAtStartup()
    {
        // Free is the fail-closed landing place for every unknown key and every tenant without a grant: a catalog without it
        // has nowhere to fail to, so the host refuses to build it rather than throw on the first unknown plan key.
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPlatformOptions(o => { o.Plans.Clear(); o.Plans.Add(new Plan("team", new HashSet<string>())); }));
        Assert.Contains(PlanKeys.Free, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPlansWithOneKey_AreRefused()
    {
        var options = new PlatformOptions();
        options.Plans.Add(new Plan(PlanKeys.Pro, new HashSet<string>()));
        var ex = Assert.Throws<InvalidOperationException>(() => TestPlans.From(options));
        Assert.Contains(PlanKeys.Pro, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultPlans_AreAFreshListPerOptions()
    {
        // An app that edits its options must not edit the platform's defaults for the next host built in the same process.
        var first = new PlatformOptions();
        first.Plans.Clear();
        Assert.Equal(2, new PlatformOptions().Plans.Count);
    }
}
