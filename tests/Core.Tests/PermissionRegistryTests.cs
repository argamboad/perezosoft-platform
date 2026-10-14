using Perezosoft.Core.Authorization;
using Perezosoft.Core.Entities;

namespace Perezosoft.Core.Tests;

/// <summary>
/// Pkg 3.8 (ADR-034 §4): <see cref="Permission"/> is an open set. The host's <see cref="PermissionRegistry"/>
/// (<c>PlatformOptions.Permissions</c>) starts as the platform's ten capabilities with their ADR-009 rows; an app adds
/// its own, which the owner holds automatically and admin/member hold only when named. Registration is additive and
/// checked when the host builds its options.
/// </summary>
public class PermissionRegistryTests
{
    private static readonly Permission ManageProjects = new("ManageProjects");

    [Fact]
    public void TheDefaultRegistry_IsThePlatformsTenPermissions_InTheEnumsOrder()
    {
        string[] enumOrder =
        [
            "ViewTenant", "RenameTenant", "ManageMembers", "ManageRoles", "ManageBilling",
            "ExportData", "TransferOwnership", "DissolveTenant", "ManageApiKeys", "ManageWebhooks",
        ];
        Assert.Equal(enumOrder, new PermissionRegistry().All.Select(p => p.Name));
        Assert.Equal(enumOrder, Permission.Platform.Select(p => p.Name));
    }

    [Fact]
    public void APermission_IsItsName()
    {
        // The 403 body reports permission.ToString(); it must stay the bare name the enum printed.
        Assert.Equal("ManageBilling", Permission.ManageBilling.ToString());
        Assert.Equal(Permission.ManageBilling, new Permission("ManageBilling"));
        Assert.NotEqual(Permission.ManageBilling, new Permission("managebilling")); // ordinal: a name is exact
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Manage Projects")]
    public void APermissionName_IsOneWord(string name) =>
        Assert.Throws<ArgumentException>(() => new Permission(name));

    [Fact]
    public void AnAppPermission_IsTheOwnersAndWhomeverItNames()
    {
        var registry = new PermissionRegistry().Add(ManageProjects, TenantRoles.Admin);
        var matrix = new RolePermissions(registry);

        Assert.True(matrix.Grants(TenantRoles.Owner, ManageProjects));
        Assert.True(matrix.Grants(TenantRoles.Admin, ManageProjects));
        Assert.False(matrix.Grants(TenantRoles.Member, ManageProjects));
        Assert.Equal(ManageProjects, matrix.All[^1]);
    }

    [Fact]
    public void AnAppPermission_NamingNoRole_IsOwnerOnly()
    {
        var matrix = new RolePermissions(new PermissionRegistry().Add(ManageProjects));
        Assert.True(matrix.Grants(TenantRoles.Owner, ManageProjects));
        Assert.False(matrix.Grants(TenantRoles.Admin, ManageProjects));
    }

    [Fact]
    public void ThePlatformsRows_CannotBeLoosened_ByRegisteringAgain()
    {
        // Billing and role/ownership changes stay owner-only: registering a platform permission again (to hand it to admins)
        // is refused, like any second registration of one name.
        var ex = Assert.Throws<InvalidOperationException>(() => new PermissionRegistry().Add(Permission.ManageBilling, TenantRoles.Admin));
        Assert.Contains("ManageBilling", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new PermissionRegistry().Add(ManageProjects).Add(new Permission("ManageProjects")));
    }

    [Theory]
    [InlineData("superadmin")]
    [InlineData("")]
    public void AnUnknownRole_IsRefused(string role) =>
        Assert.Throws<InvalidOperationException>(() => new PermissionRegistry().Add(ManageProjects, role));

    [Fact]
    public void NamingTheOwner_IsAllowed_AndChangesNothing()
    {
        var matrix = new RolePermissions(new PermissionRegistry().Add(ManageProjects, TenantRoles.Owner));
        Assert.True(matrix.Grants(TenantRoles.Owner, ManageProjects));
        Assert.False(matrix.Grants(TenantRoles.Admin, ManageProjects));
    }

    [Fact]
    public void DefaultPermission_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new PermissionRegistry().Add(default));

    [Fact]
    public void TheMatrix_IsASnapshot_OfTheRegistryAtStartup()
    {
        // The host builds its matrix once from its options; a registry edited afterwards does not change the running host.
        var registry = new PermissionRegistry();
        var matrix = new RolePermissions(registry);
        registry.Add(ManageProjects, TenantRoles.Member);
        Assert.False(matrix.IsRegistered(ManageProjects));
        Assert.False(matrix.Grants(TenantRoles.Owner, ManageProjects));
    }
}
