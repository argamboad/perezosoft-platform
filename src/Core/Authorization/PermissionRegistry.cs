using Perezosoft.Core.Entities;

namespace Perezosoft.Core.Authorization;

/// <summary>
/// The permissions a host knows and which roles grant them (Pkg 3.8, ADR-034 §4): <c>PlatformOptions.Permissions</c>. It
/// starts as the platform's matrix (ADR-009); an app adds its own capabilities with <see cref="Add"/>.
/// <list type="bullet">
/// <item><b>owner</b> holds every registered permission — the owner is the tenant's top role, so nothing is out of its reach.</item>
/// <item><b>admin</b> and <b>member</b> hold what <see cref="Add"/> names for them; nothing by default.</item>
/// </list>
/// Additive only: a permission is registered once, and the platform's rows are not editable through it (billing and
/// role/ownership changes stay owner-only — financial and privilege-escalation surfaces).
/// </summary>
public sealed class PermissionRegistry
{
    private static readonly HashSet<string> GrantableRoles = new(StringComparer.OrdinalIgnoreCase) { TenantRoles.Admin, TenantRoles.Member };

    private readonly List<Permission> _all = [];
    private readonly Dictionary<Permission, HashSet<string>> _grantedTo = [];

    /// <summary>A registry holding the platform's permissions and their ADR-009 rows.</summary>
    public PermissionRegistry()
    {
        Add(Permission.ViewTenant, TenantRoles.Admin, TenantRoles.Member);
        Add(Permission.RenameTenant, TenantRoles.Admin);
        Add(Permission.ManageMembers, TenantRoles.Admin);
        Add(Permission.ManageRoles);
        Add(Permission.ManageBilling);
        Add(Permission.ExportData);
        Add(Permission.TransferOwnership);
        Add(Permission.DissolveTenant);
        Add(Permission.ManageApiKeys);
        Add(Permission.ManageWebhooks);
    }

    /// <summary>
    /// Registers <paramref name="permission"/>, granted to the owner and to <paramref name="grantedTo"/> (admin and/or member;
    /// naming the owner is allowed and changes nothing). A name registered twice, or an unknown role, is refused — at startup,
    /// where the host's options are built.
    /// </summary>
    public PermissionRegistry Add(Permission permission, params string[] grantedTo)
    {
        if (permission.Name is null)
            throw new ArgumentException("A permission needs a name (default(Permission) has none).", nameof(permission));
        if (_grantedTo.ContainsKey(permission))
            throw new InvalidOperationException($"The permission '{permission}' is already registered — a permission name names one capability.");
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in grantedTo)
        {
            if (string.Equals(role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase)) continue; // the owner holds everything already
            if (!GrantableRoles.Contains(role))
                throw new InvalidOperationException($"'{role}' is not a tenant role; '{permission}' can be granted to '{TenantRoles.Admin}' and '{TenantRoles.Member}' (the owner holds every permission).");
            roles.Add(role);
        }
        _all.Add(permission);
        _grantedTo[permission] = roles;
        return this;
    }

    /// <summary>Every registered permission, platform first, in registration order.</summary>
    public IReadOnlyList<Permission> All => _all;

    /// <summary>Whether <paramref name="role"/> holds <paramref name="permission"/> by this registry's rows.</summary>
    internal bool IsGranted(string role, Permission permission) =>
        _grantedTo.TryGetValue(permission, out var roles)
        && (string.Equals(role, TenantRoles.Owner, StringComparison.OrdinalIgnoreCase) || roles.Contains(role));
}
