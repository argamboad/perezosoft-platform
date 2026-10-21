using Microsoft.EntityFrameworkCore;
using Perezosoft.Api.Services;
using Perezosoft.Core.Entities;
using Xunit;

using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Testing.Suites;

/// <summary>
/// The tenancy wall as an inheritable suite (Pkg 5.1b), run on the app's own test context — its model, its
/// interceptors, plus the fixture sets: the read side (the global query filter scopes every tenant-scoped set to the
/// current tenant and fails closed with none), the write side (the stamping interceptor stamps and refuses foreign
/// writes), and the membership invariants (ownership transfer, removal, leave, invitations). An app derives it once,
/// in its database collection; the platform's own class does the same.
/// </summary>
public abstract class PlatformTenancySuite<TTestContext>(PlatformPostgresFixture<TTestContext> fixture)
    : PlatformPostgresTestBase<TTestContext>(fixture)
    where TTestContext : PlatformDbContext
{
    // ── TenantScopeFilterTests ──────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Proves the EF global query filter makes tenant isolation structural: a tenant-scoped
    /// entity (<see cref="TenantInvitation"/>) is only visible to its own tenant by default,
    /// the filter fails closed when there's no current tenant, and genuinely cross-tenant
    /// lookups can still opt out with IgnoreQueryFilters().
    /// </summary>

    [Fact]
    public async Task ScopedEntity_IsVisibleOnlyToItsOwnTenant()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsEachWithAnInviteAsync();

        await using var asA = Fixture.CreateContext(tenantA);
        var visible = await asA.TenantInvitations.ToListAsync();

        Assert.Single(visible);
        Assert.Equal(tenantA, visible[0].TenantId);
    }

    [Fact]
    public async Task IgnoreQueryFilters_SeesAllTenants()
    {
        await SeedTwoTenantsEachWithAnInviteAsync();

        await using var asA = Fixture.CreateContext(Guid.CreateVersion7()); // arbitrary current tenant
        var all = await asA.TenantInvitations.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task NoCurrentTenant_SeesNoScopedRows()
    {
        await SeedTwoTenantsEachWithAnInviteAsync();

        await using var anon = Fixture.CreateContext(tenantId: null); // fail closed
        Assert.Empty(await anon.TenantInvitations.ToListAsync());
    }

    private async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsEachWithAnInviteAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        // Seed with an unscoped context so both tenants' rows are written.
        await using var seed = Fixture.CreateContext();
        seed.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = "A", CreatedAt = now, UpdatedAt = now },
            new Tenant { Id = tenantB, Name = "B", CreatedAt = now, UpdatedAt = now });
        seed.TenantInvitations.AddRange(
            NewInvite(tenantA, "a@example.com", now),
            NewInvite(tenantB, "b@example.com", now));
        await seed.SaveChangesAsync();

        return (tenantA, tenantB);
    }

    private static TenantInvitation NewInvite(Guid tenantId, string email, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        InvitedEmail = email,
        InvitedByUserId = Guid.CreateVersion7(),
        Status = InvitationStatuses.Pending,
        TokenHash = Guid.NewGuid().ToString("N"),
        CreatedAt = now,
        ExpiresAt = now.AddDays(7),
    };

    // ── TenantStampingInterceptorTests ──────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Proves tenant isolation is structural on the WRITE side too: the stamping interceptor
    /// stamps the current tenant onto a new <see cref="ITenantScoped"/> row that left
    /// <c>TenantId</c> unset, and fails closed (throws, persists nothing) if a row carries a
    /// TenantId belonging to a different tenant. Together with the read-side global query
    /// filter this closes the "a feature can silently persist under the wrong tenant" hazard
    /// (CONF-1).
    /// </summary>

    [Fact]
    public async Task Insert_WithoutTenantId_StampsCurrentTenant()
    {
        var tenant = Guid.CreateVersion7();

        await using (var db = Fixture.CreateContext(tenant))
        {
            // TenantId deliberately left default — the interceptor must stamp it.
            db.Set<TestWidget>().Add(new TestWidget { Name = "no tenant set" });
            await db.SaveChangesAsync();
        }

        await using var read = Fixture.CreateContext();
        var note = await read.Set<TestWidget>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant, note.TenantId);
    }

    [Fact]
    public async Task Insert_WithForeignTenantId_Throws_AndPersistsNothing()
    {
        var current = Guid.CreateVersion7();
        var foreignTenant = Guid.CreateVersion7();

        await using (var db = Fixture.CreateContext(current))
        {
            db.Set<TestWidget>().Add(new TestWidget { Name = "wrong tenant", TenantId = foreignTenant });
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using var read = Fixture.CreateContext();
        Assert.Empty(await read.Set<TestWidget>().IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Insert_WithMatchingTenantId_IsAllowed()
    {
        var tenant = Guid.CreateVersion7();

        await using (var db = Fixture.CreateContext(tenant))
        {
            db.Set<TestWidget>().Add(new TestWidget { Name = "explicit but correct", TenantId = tenant });
            await db.SaveChangesAsync();
        }

        await using var read = Fixture.CreateContext();
        Assert.Equal(tenant, (await read.Set<TestWidget>().IgnoreQueryFilters().SingleAsync()).TenantId);
    }

    // v2 audit ADV-1: the write guard must cover UPDATE and DELETE, not just INSERT. A row loaded
    // cross-tenant via the escape hatch must not be mutable/deletable under the wrong current tenant.

    [Fact]
    public async Task Update_ForeignRowLoadedViaHatch_Throws_AndPersistsNothing()
    {
        var (owner, attacker, noteId) = await SeedOwnedNoteAsync();

        await using (var db = Fixture.CreateContext(attacker))
        {
            var foreign = await db.Set<TestWidget>().IgnoreQueryFilters().SingleAsync(n => n.Id == noteId);
            foreign.Name = "hijacked";
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using var read = Fixture.CreateContext();
        var note = await read.Set<TestWidget>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal("owned by A", note.Name);   // unchanged
        Assert.Equal(owner, note.TenantId);
    }

    [Fact]
    public async Task Delete_ForeignRowLoadedViaHatch_Throws_AndPersistsNothing()
    {
        var (_, attacker, noteId) = await SeedOwnedNoteAsync();

        await using (var db = Fixture.CreateContext(attacker))
        {
            var foreign = await db.Set<TestWidget>().IgnoreQueryFilters().SingleAsync(n => n.Id == noteId);
            db.Set<TestWidget>().Remove(foreign);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using var read = Fixture.CreateContext();
        Assert.Single(await read.Set<TestWidget>().IgnoreQueryFilters().ToListAsync()); // still there
    }

    [Fact]
    public async Task Update_OwnRow_IsAllowed()
    {
        var tenant = Guid.CreateVersion7();
        Guid noteId;
        await using (var db = Fixture.CreateContext(tenant))
        {
            var note = new TestWidget { Name = "mine" };
            db.Set<TestWidget>().Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        await using (var db = Fixture.CreateContext(tenant))
        {
            var note = await db.Set<TestWidget>().SingleAsync(n => n.Id == noteId);
            note.Name = "renamed";
            await db.SaveChangesAsync();
        }

        await using var read = Fixture.CreateContext();
        Assert.Equal("renamed", (await read.Set<TestWidget>().IgnoreQueryFilters().SingleAsync()).Name);
    }

    /// <summary>Seeds a note owned by tenant A; returns (owner, a fresh attacker tenant, note id).</summary>
    private async Task<(Guid Owner, Guid Attacker, Guid NoteId)> SeedOwnedNoteAsync()
    {
        var owner = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(owner);
        var note = new TestWidget { Name = "owned by A" };
        db.Set<TestWidget>().Add(note);
        await db.SaveChangesAsync();
        return (owner, Guid.CreateVersion7(), note.Id);
    }

    // ── TenantInvariantTests ────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// The tenant invariants and invitation lifecycle, exercised against real Postgres so
    /// the relational ExecuteUpdateAsync/savepoint paths actually run: single-owner,
    /// always-in-exactly-one-tenant (re-home), transfer, dissolve gating, and the
    /// invite → accept → revoke → regenerate flow.
    /// </summary>

    [Fact]
    public async Task TransferOwnership_SwapsRoles()
    {
        var (oId, oTenant, mId) = await TwoMemberHouseholdAsync();

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(TransferResult.Transferred,
                await new ServiceHarness(db).TenantService().TransferOwnershipAsync(oTenant, oId, mId));

        var roles = await RolesAsync(oTenant);
        Assert.Equal(TenantRoles.Member, roles[oId]);
        Assert.Equal(TenantRoles.Owner, roles[mId]);
    }

    [Fact]
    public async Task TransferOwnership_StaleOwner_GetsConcurrentModification_SingleOwnerHolds()
    {
        // v3 TB-ADM-13 (T45a): the conditional update (role flip guarded on the caller STILL being
        // owner) is what keeps the single-owner invariant under racing transfers. A caller whose
        // ownership already moved must get ConcurrentModification (the controller maps it to 409) —
        // and crucially must NOT mint a second owner.
        var (oId, oTenant, mId) = await TwoMemberHouseholdAsync();

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(TransferResult.Transferred,
                await new ServiceHarness(db).TenantService().TransferOwnershipAsync(oTenant, oId, mId));

        // The ex-owner retries with a stale view of the world: 0 rows match the owner-guarded update.
        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(TransferResult.ConcurrentModification,
                await new ServiceHarness(db).TenantService().TransferOwnershipAsync(oTenant, oId, mId));

        var roles = await RolesAsync(oTenant);
        Assert.Equal(TenantRoles.Owner, roles[mId]);   // exactly one owner —
        Assert.Equal(TenantRoles.Member, roles[oId]);  // — and the loser stayed demoted
    }

    [Fact]
    public async Task RemoveMember_RemovesFromTenantAndReHomesAsOwner()
    {
        var (oId, oTenant, mId) = await TwoMemberHouseholdAsync();

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(RemoveMemberResult.Removed,
                await new ServiceHarness(db).TenantService().RemoveMemberAsync(oTenant, mId));

        await using var read = Fixture.CreateContext();
        Assert.False(await read.TenantMemberships.AnyAsync(m => m.TenantId == oTenant && m.UserId == mId));
        var rehomed = await read.TenantMemberships.SingleAsync(m => m.UserId == mId);
        Assert.NotEqual(oTenant, rehomed.TenantId);
        Assert.Equal(TenantRoles.Owner, rehomed.Role);
    }

    [Fact]
    public async Task Leave_AsMember_LeavesAndReHomes()
    {
        var (_, oTenant, mId) = await TwoMemberHouseholdAsync();

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(LeaveOutcome.Left,
                await new ServiceHarness(db).TenantService().LeaveAsync(mId, confirmDissolve: false));

        await using var read = Fixture.CreateContext();
        Assert.False(await read.TenantMemberships.AnyAsync(m => m.TenantId == oTenant && m.UserId == mId));
        Assert.True(await read.TenantMemberships.AnyAsync(m => m.UserId == mId)); // re-homed, never tenant-less
    }

    [Fact]
    public async Task Leave_OwnerWithMembers_MustTransferFirst()
    {
        var (oId, oTenant, _) = await TwoMemberHouseholdAsync();

        await using var db = Fixture.CreateContext(oTenant);
        Assert.Equal(LeaveOutcome.MustTransferFirst,
            await new ServiceHarness(db).TenantService().LeaveAsync(oId, confirmDissolve: false));
    }

    [Fact]
    public async Task Leave_SoleOwner_RequiresConfirmThenDissolves()
    {
        var (oId, oTenant) = await ProvisionAsync("solo@example.com");

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(LeaveOutcome.ConfirmationRequired,
                await new ServiceHarness(db).TenantService().LeaveAsync(oId, confirmDissolve: false));

        await using (var db = Fixture.CreateContext(oTenant))
            Assert.Equal(LeaveOutcome.Dissolved,
                await new ServiceHarness(db).TenantService().LeaveAsync(oId, confirmDissolve: true));

        await using var read = Fixture.CreateContext();
        Assert.False(await read.Tenants.AnyAsync(t => t.Id == oTenant)); // wiped
        var rehomed = await read.TenantMemberships.SingleAsync(m => m.UserId == oId);
        Assert.NotEqual(oTenant, rehomed.TenantId);
    }

    [Fact]
    public async Task Invite_ExistingMember_ReturnsAlreadyMember()
    {
        var (oId, oTenant, _) = await TwoMemberHouseholdAsync();

        await using var db = Fixture.CreateContext(oTenant);
        var result = await new ServiceHarness(db).InvitationService().CreateAsync(oTenant, oId, "member@example.com");
        Assert.Equal(InviteCreateStatus.AlreadyMember, result.Status);
    }

    [Fact]
    public async Task Accept_InvalidToken_ReturnsInvalidToken()
    {
        var (uId, _) = await ProvisionAsync("u@example.com");

        await using var db = Fixture.CreateContext();
        Assert.Equal(AcceptStatus.InvalidToken,
            await new ServiceHarness(db).InvitationService().AcceptAsync(uId, "garbage-token"));
    }

    [Fact]
    public async Task Revoke_ThenAccept_IsRejected()
    {
        var (oId, oTenant) = await ProvisionAsync("owner@example.com");
        var (mId, _) = await ProvisionAsync("invitee@example.com");

        string token;
        await using (var db = Fixture.CreateContext(oTenant))
        {
            var svc = new ServiceHarness(db).InvitationService();
            var created = await svc.CreateAsync(oTenant, oId, "invitee@example.com");
            token = created.RawToken!;
            Assert.True(await svc.RevokeAsync(oTenant, created.Invitation!.Id));
        }

        await using (var db = Fixture.CreateContext())
            Assert.Equal(AcceptStatus.InvalidToken,
                await new ServiceHarness(db).InvitationService().AcceptAsync(mId, token));
    }

    [Fact]
    public async Task Regenerate_OldTokenRejected_NewTokenAccepts()
    {
        var (oId, oTenant) = await ProvisionAsync("owner2@example.com");
        var (mId, _) = await ProvisionAsync("join@example.com");

        string oldToken, newToken;
        await using (var db = Fixture.CreateContext(oTenant))
        {
            var svc = new ServiceHarness(db).InvitationService();
            var created = await svc.CreateAsync(oTenant, oId, "join@example.com");
            oldToken = created.RawToken!;
            var regen = await svc.RegenerateAsync(oTenant, created.Invitation!.Id, oId);
            newToken = regen.RawToken!;
        }

        await using (var db = Fixture.CreateContext())
            Assert.Equal(AcceptStatus.InvalidToken,
                await new ServiceHarness(db).InvitationService().AcceptAsync(mId, oldToken));

        await using (var db = Fixture.CreateContext())
            Assert.Equal(AcceptStatus.Joined,
                await new ServiceHarness(db).InvitationService().AcceptAsync(mId, newToken));

        await using var read = Fixture.CreateContext();
        Assert.Equal(oTenant, (await read.TenantMemberships.SingleAsync(m => m.UserId == mId)).TenantId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(Guid UserId, Guid TenantId)> ProvisionAsync(string email)
    {
        await using var db = Fixture.CreateContext();
        var user = await new ServiceHarness(db).UserService().GetOrCreateByEmailAsync(email);
        var membership = await db.TenantMemberships.SingleAsync(m => m.UserId == user.Id);
        return (user.Id, membership.TenantId);
    }

    /// <summary>Owner + one member sharing one tenant (member joined via the real accept flow).</summary>
    private async Task<(Guid OwnerId, Guid TenantId, Guid MemberId)> TwoMemberHouseholdAsync()
    {
        var (oId, oTenant) = await ProvisionAsync("owner@example.com");
        var (mId, _) = await ProvisionAsync("member@example.com");

        string token;
        await using (var db = Fixture.CreateContext(oTenant))
            token = (await new ServiceHarness(db).InvitationService()
                .CreateAsync(oTenant, oId, "member@example.com")).RawToken!;

        // Accept runs while the member's JWT still holds their old tenant — the
        // invitation lookups must bypass the global filter (proves Phase 1's exemption).
        await using (var db = Fixture.CreateContext())
            Assert.Equal(AcceptStatus.Joined,
                await new ServiceHarness(db).InvitationService().AcceptAsync(mId, token));

        return (oId, oTenant, mId);
    }

    private async Task<Dictionary<Guid, string>> RolesAsync(Guid tenantId)
    {
        await using var read = Fixture.CreateContext();
        return await read.TenantMemberships.Where(m => m.TenantId == tenantId)
            .ToDictionaryAsync(m => m.UserId, m => m.Role);
    }
}
