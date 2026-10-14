namespace Perezosoft.Core.Authorization;

/// <summary>
/// A coarse tenant capability (ADR-009), named. Authorization asks "does the caller's role grant this
/// <see cref="Permission"/>?" via <see cref="RolePermissions"/> — never a scattered <c>role == "owner"</c> test. These are
/// capabilities, <b>not</b> per-resource ACLs; fine-grained per-record sharing is a separate, deferred concern (don't grow
/// this into an ACL system without a new ADR).
/// <para>
/// Open since Pkg 3.8 (ADR-034 §4): the platform's own capabilities are the static members below; an app adds its own in
/// <c>AddPlatform(o => o.Permissions.Add(new Permission("ManageProjects"), TenantRoles.Admin))</c>, and gates endpoints with
/// it exactly like a platform one. Two permissions are the same permission when their names are (ordinal); the name is
/// what a 403 body reports and what <c>[RequireTenantPermission]</c> takes, since an attribute argument must be a constant
/// — <c>nameof(Permission.ManageBilling)</c> for a platform one.
/// </para>
/// </summary>
public readonly record struct Permission
{
    public Permission(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
            throw new ArgumentException($"A permission name is one word with no whitespace (got \"{name}\").", nameof(name));
        Name = name;
    }

    /// <summary>The permission's name: its identity, and what a 403 body reports.</summary>
    public string Name { get; }

    public override string ToString() => Name;

    /// <summary>Read the tenant and its member roster. Every role has this.</summary>
    public static readonly Permission ViewTenant = new(nameof(ViewTenant));

    /// <summary>Rename the tenant.</summary>
    public static readonly Permission RenameTenant = new(nameof(RenameTenant));

    /// <summary>Invite, remove, and manage members and their invitations.</summary>
    public static readonly Permission ManageMembers = new(nameof(ManageMembers));

    /// <summary>Promote/demote members between admin and member (owner-only — privilege escalation).</summary>
    public static readonly Permission ManageRoles = new(nameof(ManageRoles));

    /// <summary>Start checkout and open the billing portal (owner-only — financial).</summary>
    public static readonly Permission ManageBilling = new(nameof(ManageBilling));

    /// <summary>Export the tenant's data — "download my data" (owner-only; GDPR, ADR-011).</summary>
    public static readonly Permission ExportData = new(nameof(ExportData));

    /// <summary>Transfer ownership to another member (owner-only).</summary>
    public static readonly Permission TransferOwnership = new(nameof(TransferOwnership));

    /// <summary>Dissolve the tenant and delete its data (owner-only).</summary>
    public static readonly Permission DissolveTenant = new(nameof(DissolveTenant));

    /// <summary>Create/revoke the tenant's API keys — programmatic tenant access (owner-only; PUBAPI).</summary>
    public static readonly Permission ManageApiKeys = new(nameof(ManageApiKeys));

    /// <summary>Register/remove the tenant's outbound webhook subscriptions (owner-only; HOOKS).</summary>
    public static readonly Permission ManageWebhooks = new(nameof(ManageWebhooks));

    /// <summary>The platform's own permissions, in the order the enum declared them.</summary>
    public static IReadOnlyList<Permission> Platform { get; } =
    [
        ViewTenant, RenameTenant, ManageMembers, ManageRoles, ManageBilling,
        ExportData, TransferOwnership, DissolveTenant, ManageApiKeys, ManageWebhooks,
    ];
}
