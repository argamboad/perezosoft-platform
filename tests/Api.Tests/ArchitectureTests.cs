using System.Reflection;
using Perezosoft.Api.Tests.App;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Testing.Suites;

namespace Perezosoft.Api.Tests;

/// <summary>
/// The platform's architecture gates: every rule of <see cref="PlatformArchitectureSuite"/> (Pkg 5.1b) run against this
/// repo, plus the few that read the platform's own sources. An app derives the same suite and says the same things.
/// </summary>
public class ArchitectureTests : PlatformArchitectureSuite
{
    protected override Assembly HostAssembly => typeof(Perezosoft.Api.AppComposition).Assembly;
    protected override PlatformDbContext CreateModelContext() => new AppDbContext(ModelOptions<AppDbContext>(), new TestCurrentTenant());
    protected override PlatformDbContext CreateTestModelContext() => new TestAppDbContext(ModelOptions<TestAppDbContext>(), new TestCurrentTenant());

    protected override IReadOnlySet<string> ErasureHandled => AppAllowlists.ErasureHandled;
    protected override IReadOnlySet<string> DissolutionHandled => AppAllowlists.DissolutionHandled;
    protected override IReadOnlyDictionary<string, string> LifecycleSpecExceptions => AppAllowlists.LifecycleSpecExceptions;
    protected override IReadOnlySet<string> TenantIdByConvention => AppAllowlists.TenantIdByConvention;
    protected override IReadOnlyDictionary<string, string> OutboundHttpSenders => AppAllowlists.OutboundHttpSenders;
    protected override IReadOnlySet<string> ControllersOutsideTheBases => AppAllowlists.ControllersOutsideTheBases;
    protected override IReadOnlyDictionary<string, string> EntityWriters => AppAllowlists.EntityWriters;
    protected override IReadOnlyDictionary<string, string> EnterTenantRowSupplied => AppAllowlists.EnterTenantRowSupplied;
    protected override IReadOnlySet<string> TypeNameExceptions => AppAllowlists.TypeNameExceptions;

    // ── The platform's own: rules that read the platform's sources ─────────────────────────────────────────────────

    [Fact]
    public void TenantDissolution_EntersTheTargetTenant()
    {
        // RLS-2/R44: DissolveAsync runs set-based deletes that the Postgres RLS backstop (ADR-020) scopes to
        // the AMBIENT tenant. To stay correct when a caller dissolves a tenant other than its ambient one
        // (account erasure of a solo tenant that isn't the JWT-current tenant, admin paths), the service must
        // enter the target so every delete is scoped to it — instead of relying on the CrossTenant-tag bypass
        // (which renders for ExecuteDelete but not ExecuteUpdate) and the Tenants FK cascade. Pin that it does.
        var file = Path.Combine(Root, "src", "Api", "Services", "TenantDissolutionService.cs");
        var text = File.ReadAllText(file);
        Assert.Contains("EnterTenant(", text);
    }

    [Fact]
    public void YearFormatGate_SeesFormats_NotMessages() // self-test of the predicate above
    {
        Assert.True(IsCultureSensitiveYearFormat("""var period = now.ToString("yyyy-MM");"""));
        Assert.True(IsCultureSensitiveYearFormat("""var renews = $" It renews on {end:yyyy-MM-dd}.";"""));
        Assert.True(IsCultureSensitiveYearFormat("""var key = $"exports/{clock.GetUtcNow():yyyyMMddTHHmmssZ}-{id:N}.json";"""));
        Assert.False(IsCultureSensitiveYearFormat("""var period = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);"""));
        Assert.False(IsCultureSensitiveYearFormat("""return BadRequest(new ErrorResponse("invalid_request", "date is required (yyyy-MM-dd)"));"""));
        Assert.False(IsCultureSensitiveYearFormat("""private static readonly string[] Formats = ["d/M/yyyy", "d-M-yy"];"""));
        Assert.False(IsCultureSensitiveYearFormat("""private string Date(DateOnly d) => d.ToString("d MMM yyyy", _c);"""));
    }

    [Fact]
    public void PlatformTests_DoNotDependOnTheDeleteMeNotesSample()
    {
        // R9/TR-1: the tenancy/GDPR/outbox tests use the harness TestWidget fixture, not the DELETE-ME
        // Notes sample — so a downstream app can delete the sample without breaking the isolation guards.
        var testsDir = Path.Combine(Root, "tests");
        string[] banned = ["Features.Notes", "Set<Note>", "new Note", "EfRepository<Note>", "IRepository<Note>"];

        // NotesSliceTests legitimately tests the sample; this file and the composition-seam gate in EnforcementGateTests
        // (CompositionFiles_AreFreeOfTheSampleSlice, Arch A1) list the banned patterns as literals.
        var exempt = new HashSet<string> { "NotesSliceTests.cs", "ArchitectureTests.cs", "EnforcementGateTests.cs" };
        var offenders = SourceFiles(testsDir)
            .Where(f => !exempt.Contains(Path.GetFileName(f)!))
            .Where(f => File.ReadAllText(f) is var t && Array.Exists(banned, t.Contains))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Platform tests must use the harness TestWidget fixture, not the DELETE-ME Notes sample: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void IntegrationFactory_DoesNotBackfillRlsPolicies()
    {
        // v3 audit RLS-1: the migration-parity gate (RlsMigrationGateTests) is only honest if the database
        // it inspects gets its RLS policies from MIGRATIONS, not back-filled from the model. So the
        // migration-based harness must provision the runtime ROLE only — never RlsDdl.StatementsFor, and
        // never the policy-applying RlsTestSetup.ProvisionAsync. If it did, a slice could ship an
        // ITenantScoped table with no RLS migration and still pass CI, reaching production unprotected.
        // The harness lives in the package since Pkg 5.1a; its logic is PlatformIntegrationFactory (the in-tree class is a
        // one-line subclass, which would pass this check while checking nothing).
        var factory = Path.Combine(Root, "src", "Testing", "Harness", "PlatformIntegrationFactory.cs");
        var text = File.ReadAllText(factory);

        Assert.DoesNotContain("RlsDdl.StatementsFor", text);
        Assert.DoesNotContain("RlsTestSetup.ProvisionAsync", text);
    }
}
