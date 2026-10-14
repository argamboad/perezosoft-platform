using Perezosoft.Core.Authorization;
using Perezosoft.Core.Entities;

namespace Perezosoft.Core.Tests;

/// <summary>
/// RBAC-1 (ADR-009): the <see cref="RolePermissions"/> matrix is the single source of truth mapping a
/// tenant role to the capabilities it grants. owner > admin > member; billing and role/ownership
/// changes are owner-only by design; unknown roles grant nothing (fail closed). Since Pkg 3.8 the matrix is
/// built per host from its <see cref="PermissionRegistry"/>; these rows are the platform's default registry.
/// The theories name permissions as strings because an attribute argument must be a constant.
/// </summary>
public class RolePermissionsTests
{
    private static readonly RolePermissions Matrix = new(new PermissionRegistry());

    [Theory]
    [InlineData(nameof(Permission.ViewTenant))]
    [InlineData(nameof(Permission.RenameTenant))]
    [InlineData(nameof(Permission.ManageMembers))]
    [InlineData(nameof(Permission.ManageRoles))]
    [InlineData(nameof(Permission.ManageBilling))]
    [InlineData(nameof(Permission.ExportData))]
    [InlineData(nameof(Permission.TransferOwnership))]
    [InlineData(nameof(Permission.DissolveTenant))]
    public void Owner_HasEveryPermission(string permission) =>
        Assert.True(Matrix.Grants(TenantRoles.Owner, new Permission(permission)));

    [Theory]
    [InlineData(nameof(Permission.ViewTenant), true)]
    [InlineData(nameof(Permission.RenameTenant), true)]
    [InlineData(nameof(Permission.ManageMembers), true)]
    [InlineData(nameof(Permission.ManageRoles), false)]
    [InlineData(nameof(Permission.ManageBilling), false)]
    [InlineData(nameof(Permission.ExportData), false)]
    [InlineData(nameof(Permission.TransferOwnership), false)]
    [InlineData(nameof(Permission.DissolveTenant), false)]
    public void Admin_HasManagementButNotOwnerOnlyPermissions(string permission, bool granted) =>
        Assert.Equal(granted, Matrix.Grants(TenantRoles.Admin, new Permission(permission)));

    [Theory]
    [InlineData(nameof(Permission.ViewTenant), true)]
    [InlineData(nameof(Permission.RenameTenant), false)]
    [InlineData(nameof(Permission.ManageMembers), false)]
    [InlineData(nameof(Permission.ManageRoles), false)]
    [InlineData(nameof(Permission.ManageBilling), false)]
    [InlineData(nameof(Permission.ExportData), false)]
    [InlineData(nameof(Permission.TransferOwnership), false)]
    [InlineData(nameof(Permission.DissolveTenant), false)]
    public void Member_HasOnlyViewTenant(string permission, bool granted) =>
        Assert.Equal(granted, Matrix.Grants(TenantRoles.Member, new Permission(permission)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("superadmin")]
    public void UnknownRole_GrantsNothing(string? role)
    {
        foreach (var permission in Matrix.All)
            Assert.False(Matrix.Grants(role, permission));
    }

    [Theory]
    [InlineData("OWNER")]
    [InlineData("Admin")]
    [InlineData("Member")]
    public void RoleMatching_IsCaseInsensitive(string role) =>
        Assert.True(Matrix.Grants(role, Permission.ViewTenant));

    [Fact]
    public void Ordering_OwnerSupersetOfAdmin_SupersetOfMember()
    {
        var owner = Matrix.PermissionsFor(TenantRoles.Owner);
        var admin = Matrix.PermissionsFor(TenantRoles.Admin);
        var member = Matrix.PermissionsFor(TenantRoles.Member);

        Assert.True(admin.IsProperSubsetOf(owner));
        Assert.True(member.IsProperSubsetOf(admin));
    }

    [Fact]
    public void APermissionNobodyRegistered_GrantsNothing_EvenToTheOwner()
    {
        var unregistered = new Permission("NobodyRegisteredThis");
        Assert.False(Matrix.IsRegistered(unregistered));
        Assert.False(Matrix.Grants(TenantRoles.Owner, unregistered));
        Assert.False(Matrix.Grants(TenantRoles.Owner, default)); // default(Permission) names nothing
    }
}
