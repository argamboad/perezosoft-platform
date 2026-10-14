using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Perezosoft.Api.Controllers;

namespace Perezosoft.Api.Configuration;

/// <summary>
/// GATES-1 (ADR-027): when <see cref="BillingSettings.Enabled"/> is false, drops the billing controllers
/// out of the MVC application model so their routes are never built. The result is a genuine <b>404</b> —
/// the same posture PUBAPI/HOOKS get by simply not calling their <c>Map…</c> helpers, which billing could
/// not have because it is attribute-routed through <c>MapControllers()</c>.
/// <para>
/// A per-action filter was the obvious alternative and is weaker: it leaves the routes in the table, and
/// every future billing endpoint has to remember to wear it. Removing the controllers makes the gate
/// hold for endpoints nobody has written yet.
/// </para>
/// <para>
/// The provider webhook is gated too. It is anonymous and signature-authenticated, which is not a reason
/// to accept it while billing is off: with no provider account behind the deployment there is no event
/// worth applying, and an open callback is surface for nothing.
/// </para>
/// </summary>
public sealed class BillingGateConvention : IApplicationModelConvention
{
    private readonly BillingSettings _settings;

    /// <param name="settings">The gate.</param>
    /// <param name="appGated">The app's own billing-only controllers (Pkg 3.10, <c>PlatformOptions.BillingGatedControllers</c>),
    /// gated alongside the platform's. Each must be an MVC controller: anything else could never be removed, and the gate
    /// would quietly not hold for it.</param>
    public BillingGateConvention(BillingSettings settings, IEnumerable<Type>? appGated = null)
    {
        _settings = settings;
        var gated = new HashSet<Type>(GatedControllers);
        foreach (var type in appGated ?? [])
        {
            if (!typeof(ControllerBase).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException(
                    $"PlatformOptions.BillingGatedControllers holds {type.FullName}, which is not an MVC controller — only a controller can be gated off.");
            gated.Add(type);
        }
        Gated = gated;
    }

    /// <summary>Every controller this gate removes while billing is off: the platform's plus the app's.</summary>
    public IReadOnlySet<Type> Gated { get; }

    /// <summary>
    /// The controllers that exist only when billing is on. Public because it is an invariant, not a
    /// detail: <c>BillingControllers_AreAllGated</c> asserts every controller routed under
    /// <c>api/billing</c> appears here, which is what lets the provider registration relax its
    /// production fail-fast when the gate is off (a gated-off deployment has no reachable webhook).
    /// </summary>
    public static readonly IReadOnlySet<Type> GatedControllers = new HashSet<Type>
    {
        typeof(BillingController),
        typeof(BillingWebhookController),
    };

    public void Apply(ApplicationModel application)
    {
        if (_settings.Enabled) return;

        foreach (var controller in application.Controllers
                     .Where(c => Gated.Contains(c.ControllerType.AsType()))
                     .ToList())
        {
            application.Controllers.Remove(controller);
        }
    }
}
