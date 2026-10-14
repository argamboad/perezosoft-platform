using Perezosoft.Core.Authorization;
using Perezosoft.Core.Billing;

namespace Perezosoft.Core;

/// <summary>
/// What a host tells the platform about itself (Pkg 3.7, ADR-034 §4): the options an app sets in
/// <c>builder.AddPlatform&lt;TContext&gt;(o => ...)</c> instead of editing platform code. Every property starts at the
/// platform's own default, so a host that sets nothing behaves exactly as before. Later seams add their members here
/// (permissions, the email brand, route prefixes); a new member is additive, never a change to an existing one.
/// </summary>
public sealed class PlatformOptions
{
    /// <summary>
    /// The plan tiers (ADR-006): entitlements and quotas per plan key. Starts as the platform's catalog (Free and Pro);
    /// an app edits the list or replaces it (<c>Clear()</c>, then <c>Add</c>). It must keep a <c>free</c> plan, the
    /// fail-closed landing place for any unknown key — the host refuses to start without one.
    /// </summary>
    public IList<Plan> Plans { get; } = PlanCatalog.DefaultPlans();

    /// <summary>
    /// The tenant permissions and which roles grant them (ADR-009, Pkg 3.8). Starts as the platform's matrix; an app adds its
    /// own capabilities with <c>Permissions.Add(new Permission("ManageProjects"), TenantRoles.Admin)</c> — the owner holds every
    /// permission. The platform's rows cannot be loosened here.
    /// </summary>
    public PermissionRegistry Permissions { get; } = new();
}
