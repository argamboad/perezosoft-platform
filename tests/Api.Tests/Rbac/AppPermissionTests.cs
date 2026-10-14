using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Authentication;
using Perezosoft.Api.Configuration;
using Perezosoft.Api.Endpoints;
using Perezosoft.Api.Services;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core;
using Perezosoft.Core.Authorization;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Repositories;

namespace Perezosoft.Api.Tests.Rbac;

/// <summary>
/// Pkg 3.8 (ADR-034 §4): an app's own permission, registered in <c>AddPlatform(o => o.Permissions.Add(...))</c>, gates an
/// app's action through <see cref="RequireTenantPermissionAttribute"/> exactly like a platform permission gates a
/// platform action: the owner always passes, the roles it names pass, the others get the same 403 envelope.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AppPermissionTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    private const string ManageProjects = "ManageProjects";

    /// <summary>A scratch app's action, gated on its own permission (a plain class: the gate needs only the attribute).</summary>
    private sealed class ScratchAppProjects
    {
        [RequireTenantPermission(ManageProjects, "Only an owner or admin can manage projects")]
        public void Archive() { }
    }

    [Fact]
    public async Task AnAppDefinedPermission_PassesRequireTenantPermission_ForTheRolesItNames()
    {
        var services = new ServiceCollection();
        services.AddPlatformOptions(o => o.Permissions.Add(new Permission(ManageProjects), TenantRoles.Admin));
        using var provider = services.BuildServiceProvider();
        var roles = provider.GetRequiredService<RolePermissions>();

        var tenantId = Guid.CreateVersion7();
        var ownerId = await SeedMembershipAsync(tenantId, TenantRoles.Owner);
        var adminId = await SeedMembershipAsync(tenantId, TenantRoles.Admin);
        var memberId = await SeedMembershipAsync(tenantId, TenantRoles.Member);
        await using var db = Fixture.CreateContext();
        var tenants = new TenantRepository(db);

        Assert.Null(await TenantPermissionGate.RunAsync(typeof(ScratchAppProjects), nameof(ScratchAppProjects.Archive), tenants, ownerId, tenantId, roles));
        Assert.Null(await TenantPermissionGate.RunAsync(typeof(ScratchAppProjects), nameof(ScratchAppProjects.Archive), tenants, adminId, tenantId, roles));
        var denied = Assert.IsType<ObjectResult>(
            await TenantPermissionGate.RunAsync(typeof(ScratchAppProjects), nameof(ScratchAppProjects.Archive), tenants, memberId, tenantId, roles));
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal("forbidden", Assert.IsType<ErrorResponse>(denied.Value).Error);
    }

    [Fact]
    public async Task AnUnregisteredPermission_GrantsNobody_NotEvenTheOwner()
    {
        // The platform's default host never registered ManageProjects: the gate fails closed. (PermissionRegistrationGateTests
        // stops such a gate from shipping; this is what happens if one does.)
        var tenantId = Guid.CreateVersion7();
        var ownerId = await SeedMembershipAsync(tenantId, TenantRoles.Owner);
        await using var db = Fixture.CreateContext();

        var denied = Assert.IsType<ObjectResult>(await TenantPermissionGate.RunAsync(
            typeof(ScratchAppProjects), nameof(ScratchAppProjects.Archive), new TenantRepository(db), ownerId, tenantId));
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public void AddPlatformOptions_RegistersTheHostsMatrix_AndRefusesABadRegistration()
    {
        var services = new ServiceCollection();
        services.AddPlatformOptions();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(Permission.Platform, provider.GetRequiredService<RolePermissions>().All);

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPlatformOptions(o => o.Permissions.Add(Permission.ManageBilling, TenantRoles.Admin)));
    }

    private async Task<Guid> SeedMembershipAsync(Guid tenantId, string role)
    {
        var userId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext();
        if (!await db.Set<Tenant>().AnyAsync(t => t.Id == tenantId))
            db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "Tenant" });
        db.Set<User>().Add(new User { Id = userId, Email = $"u-{userId:N}@x.com" });
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = userId, Role = role });
        await db.SaveChangesAsync();
        return userId;
    }
}

/// <summary>
/// Pkg 3.8: every permission a running host's endpoints require — <c>[RequireTenantPermission]</c> on an action,
/// <c>.RequirePermission(...)</c> on a minimal API — is one the host registered. A permission is a name now, so a typo or
/// a forgotten <c>o.Permissions.Add(...)</c> would compile and then deny everyone; this fails first.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PermissionRegistrationGateTests(IntegrationTestFactory factory)
{
    [Fact]
    public void EveryRequiredPermission_IsRegistered_WithEveryGateOn()
    {
        using var allOn = factory.WithGates([.. IntegrationTestFactory.AllGates()]); // the gated surfaces (API keys, webhooks, billing) too
        var roles = allOn.Services.GetRequiredService<RolePermissions>();
        var required = Required(allOn.Services);

        Assert.True(required.Count >= 14, $"probe: only {required.Count} permission gates found in the route table — did the metadata move?");
        Assert.Contains(required, r => r.Permission == Permission.ManageWebhooks); // a minimal-API gate
        Assert.Contains(required, r => r.Permission == Permission.ManageBilling);  // an MVC attribute gate
        var unregistered = Unregistered(required, roles);
        Assert.True(unregistered.Count == 0,
            "an endpoint requires a permission this host never registered — add it in AddPlatform(o => o.Permissions.Add(...)) or fix the name: "
            + string.Join(", ", unregistered));
    }

    [Fact]
    public void AnUnregisteredRequirement_IsReported()
    {
        var unregistered = Unregistered([("GET /api/projects", new Permission("ManageProjects"))], TestPermissions.Default);
        Assert.Equal(["GET /api/projects: ManageProjects"], unregistered);
    }

    private static List<(string Endpoint, Permission Permission)> Required(IServiceProvider services) =>
        [.. services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .SelectMany(e => e.Metadata.OfType<RequireTenantPermissionAttribute>().Select(a => a.Permission)
                .Concat(e.Metadata.OfType<RequiredTenantPermission>().Select(m => m.Permission))
                .Select(p => (Endpoint: e.DisplayName ?? "?", Permission: p)))];

    private static List<string> Unregistered(IEnumerable<(string Endpoint, Permission Permission)> required, RolePermissions roles) =>
        [.. required.Where(r => !roles.IsRegistered(r.Permission)).Select(r => $"{r.Endpoint}: {r.Permission}").Distinct()];
}
