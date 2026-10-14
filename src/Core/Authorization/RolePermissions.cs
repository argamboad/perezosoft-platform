using Perezosoft.Core.Entities;

namespace Perezosoft.Core.Authorization;

/// <summary>
/// The single source of truth mapping a tenant role to the set of <see cref="Permission"/>s it grants
/// (ADR-009). Roles are ordered <c>owner</c> &gt; <c>admin</c> &gt; <c>member</c>: owner gets every
/// permission, admin gets the management subset, member can only view. Billing and role/ownership
/// changes are owner-only by design (financial / privilege-escalation surface). Unknown roles grant
/// nothing (fail closed), and so does a permission nobody registered. Change authorization in the host's
/// <c>PlatformOptions.Permissions</c> — never by adding a <c>role == "admin"</c> check elsewhere.
/// <para>
/// Built once per host from its <see cref="PermissionRegistry"/> (Pkg 3.8) and registered as a singleton: the
/// MVC attribute, the minimal-API filter and <c>IPermissionService</c> all ask this one instance.
/// </para>
/// </summary>
public sealed class RolePermissions
{
    private static readonly IReadOnlySet<Permission> None = new HashSet<Permission>();

    private readonly IReadOnlyDictionary<string, IReadOnlySet<Permission>> _byRole;

    public RolePermissions(PermissionRegistry registry)
    {
        All = [.. registry.All];
        _byRole = new[] { TenantRoles.Owner, TenantRoles.Admin, TenantRoles.Member }.ToDictionary(
            role => role,
            role => (IReadOnlySet<Permission>)All.Where(p => registry.IsGranted(role, p)).ToHashSet(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every permission this host knows, platform first.</summary>
    public IReadOnlyList<Permission> All { get; }

    /// <summary>The permissions granted to <paramref name="role"/>; empty for null/unknown roles.</summary>
    public IReadOnlySet<Permission> PermissionsFor(string? role) =>
        role is not null && _byRole.TryGetValue(role, out var permissions) ? permissions : None;

    /// <summary>True if <paramref name="role"/> grants <paramref name="permission"/>.</summary>
    public bool Grants(string? role, Permission permission) =>
        PermissionsFor(role).Contains(permission);

    /// <summary>Whether the host registered a permission of this name — a gate naming any other can never be passed.</summary>
    public bool IsRegistered(Permission permission) => All.Contains(permission);
}
