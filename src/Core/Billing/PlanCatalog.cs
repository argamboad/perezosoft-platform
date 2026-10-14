namespace Perezosoft.Core.Billing;

/// <summary>
/// A plan tier: the entitlement keys it grants (feature on/off) plus its **quotas** — countable limits
/// on seats and metered usage (BILLING-5). Plans are **code/config, not tenant data** (ADR-006).
/// A <c>null</c> seat limit or an absent usage key means <b>unlimited</b> — so quotas stay inert until a
/// plan sets a real number.
/// </summary>
public sealed record Plan(
    string Key,
    IReadOnlySet<string> Entitlements,
    int? SeatLimit = null,
    IReadOnlyDictionary<string, int>? UsageLimits = null)
{
    public bool Includes(string entitlementKey) => Entitlements.Contains(entitlementKey);

    /// <summary>The monthly limit for a metered-usage key, or <c>null</c> (unlimited) if the plan sets none.</summary>
    public int? UsageLimit(string usageKey) =>
        UsageLimits is not null && UsageLimits.TryGetValue(usageKey, out var limit) ? limit : null;
}

/// <summary>Plan-tier keys. <c>Free</c> is the fail-closed default for any tenant without an active paid plan.</summary>
public static class PlanKeys
{
    public const string Free = "free";
    public const string Pro = "pro";
}

/// <summary>
/// Entitlement (feature-gate) keys. These are **example** keys for the platform — replace them with the
/// app's real feature gates, then gate endpoints with <c>.RequireEntitlement(Entitlements.Xxx)</c>.
/// </summary>
public static class Entitlements
{
    public const string ProFeature = "pro-feature";
}

/// <summary>
/// Metered-usage keys (BILLING-5) — countable actions capped per month by a plan's <c>UsageLimits</c> and
/// enforced by calling <c>IQuotaService.TryConsumeAsync(key)</c> at the action. **Example** for the
/// platform; replace with the app's real metered actions.
/// </summary>
public static class UsageKeys
{
    public const string Export = "export";
}

/// <summary>
/// The plan catalog: plan key → <see cref="Plan"/>, built once per host from <c>PlatformOptions.Plans</c> (Pkg 3.7).
/// <see cref="Get"/> fails closed — an unknown plan key resolves to Free — which is why the constructor refuses a set of
/// plans without one, and why two plans with one key are refused rather than letting the later quietly win.
/// </summary>
public sealed class PlanCatalog
{
    private readonly IReadOnlyDictionary<string, Plan> _plans;

    public PlanCatalog(IEnumerable<Plan> plans)
    {
        var byKey = new Dictionary<string, Plan>(StringComparer.Ordinal);
        foreach (var plan in plans)
            if (!byKey.TryAdd(plan.Key, plan))
                throw new InvalidOperationException($"Two plans share the key '{plan.Key}' — a plan key names exactly one plan.");
        if (!byKey.ContainsKey(PlanKeys.Free))
            throw new InvalidOperationException(
                $"The plan catalog has no '{PlanKeys.Free}' plan. Free is where every unknown plan key and every tenant without a grant fails closed to — keep it in PlatformOptions.Plans.");
        _plans = byKey;
    }

    /// <summary>Resolves a plan by key; unknown keys fall back to <see cref="PlanKeys.Free"/> (fail-closed).</summary>
    public Plan Get(string planKey) => _plans.TryGetValue(planKey, out var plan) ? plan : _plans[PlanKeys.Free];

    /// <summary>
    /// The platform's own plans, a fresh list each call (so one host's edits never reach the next): the starting value of
    /// <c>PlatformOptions.Plans</c>.
    /// </summary>
    public static List<Plan> DefaultPlans() =>
    [
        // EXAMPLE quotas (BILLING-5): tune these per app, or set null/omit for unlimited. Seats = tenant
        // members + pending invites; usage keys are monthly, enforced where you call TryConsumeAsync.
        // Free's seat limit is 5, raised from 3 by GATES-1 (ADR-027): three was exactly one household
        // with zero headroom, and a merely PENDING invitation already consumes a seat — so a private
        // test could not add a fourth person at all.
        new(PlanKeys.Free, new HashSet<string>(),
            SeatLimit: 5,
            UsageLimits: new Dictionary<string, int> { [UsageKeys.Export] = 3 }),
        new(PlanKeys.Pro, new HashSet<string> { Entitlements.ProFeature },
            SeatLimit: 10,
            UsageLimits: new Dictionary<string, int> { [UsageKeys.Export] = 100 }),
    ];
}
