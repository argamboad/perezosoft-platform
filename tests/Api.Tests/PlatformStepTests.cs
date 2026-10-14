using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 3.6 (ADR-034 §5): what EF cannot scaffold from a model change (an RLS policy, a data step) ships as a published
/// platform step that a migration calls by name, <c>migrationBuilder.ApplyPlatformStep(name)</c>. A package release that
/// publishes a step names it in its release note; the app's next migration calls it. Two gates hold the contract:
/// every published step is applied by some migration in this repo (a step a release added and the app forgot fails
/// here, not as a missing policy in production), and a published step never changes (a migration that already ran
/// applied the old SQL; a fresh database must get the same).
/// </summary>
public class PlatformStepTests
{
    /// <summary>EF's active-provider name for Postgres: the Npgsql provider's assembly name, as EF itself reads it.</summary>
    private static readonly string Provider = typeof(Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions).Assembly.GetName().Name!;

    [Fact]
    public void EveryPublishedStep_IsAppliedByAMigration()
    {
        var missing = PlatformStepGate.Unapplied(PlatformSteps.Published, typeof(AppDbContext).Assembly);
        Assert.True(missing.Count == 0,
            "a published platform step no migration applies — add a migration that calls migrationBuilder.ApplyPlatformStep(name) "
            + "(the package's release note names it): " + string.Join(", ", missing));
    }

    [Fact]
    public void AStepWithoutAMigration_FailsTheGate()
    {
        // The gate bites: a step published without a migration that applies it is reported by name.
        var unpublished = new PlatformStep("NoMigrationCallsThis", "a step for the gate's own test", ["SELECT 1;"], ["SELECT 1;"]);
        var missing = PlatformStepGate.Unapplied([.. PlatformSteps.Published, unpublished], typeof(AppDbContext).Assembly);
        Assert.Equal(["NoMigrationCallsThis"], missing);
    }

    [Fact]
    public void PublishedSteps_NeverChange()
    {
        // A migration that already ran applied the step's SQL as it was; a fresh database must get the same. A change in
        // what a step emits (e.g. RlsDdl's output) is a NEW step under a new name, never an edit of a published one.
        Assert.NotEmpty(PlatformSteps.Published);
        foreach (var step in PlatformSteps.Published)
            Assert.True(step.Fingerprint() == PlatformSteps.RecordedFingerprints[step.Name],
                $"platform step '{step.Name}' changed what it emits (fingerprint {step.Fingerprint()}) — publish a new step instead");
        Assert.Equal(PlatformSteps.Published.Select(s => s.Name).Order(), PlatformSteps.RecordedFingerprints.Keys.Order());
    }

    [Fact]
    public void StepNames_AreUnique()
    {
        var names = PlatformSteps.Published.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ApplyPlatformStep_EmitsTheStepsSql_MarkedWithItsName()
    {
        var step = PlatformSteps.Get(PlatformSteps.RlsTenancyBackstop);
        var builder = new MigrationBuilder(Provider);
        builder.ApplyPlatformStep(PlatformSteps.RlsTenancyBackstop);

        var sql = builder.Operations.Cast<SqlOperation>().Select(o => o.Sql).ToList();
        Assert.Equal(step.Up.Count, sql.Count);
        Assert.StartsWith(PlatformSteps.Marker(step.Name), sql[0], StringComparison.Ordinal);
        Assert.Equal(step.Up, [sql[0][PlatformSteps.Marker(step.Name).Length..].TrimStart(), .. sql.Skip(1)]);

        var down = new MigrationBuilder(Provider);
        down.RevertPlatformStep(PlatformSteps.RlsTenancyBackstop);
        Assert.Equal(step.Down, down.Operations.Cast<SqlOperation>().Select(o => o.Sql));
    }

    [Fact]
    public void AnUnknownStep_FailsAtTheMigration_NotSilently()
    {
        var builder = new MigrationBuilder(Provider);
        var ex = Assert.Throws<InvalidOperationException>(() => builder.ApplyPlatformStep("NoSuchStep"));
        Assert.Contains("NoSuchStep", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBackstopStep_IsTheTenantPolicy_OnThePlatformsTables()
    {
        // The step froze RlsTenancyBackstop's list: the platform's ITenantScoped tables as of 2026-07-06, minus the sample's
        // (Arch A6: the Notes policy is the sample's own migration). Each gets RlsDdl's tenant policy.
        var step = PlatformSteps.Get(PlatformSteps.RlsTenancyBackstop);
        string[] tables = ["ApiKeys", "AuditEvents", "Subscriptions", "TenantInvitations", "UsageCounters", "WebhookSubscriptions"];
        Assert.Equal(tables.SelectMany(t => RlsDdl.StatementsFor(t, "TenantId")), step.Up);
        Assert.Equal(tables.SelectMany(t => RlsDdl.DropStatementsFor(t)), step.Down);
        Assert.DoesNotContain(step.Up, s => s.Contains("\"Notes\"", StringComparison.Ordinal));
    }
}

/// <summary>Which published steps no migration in an assembly applies — read from the operations EF would run.</summary>
internal static class PlatformStepGate
{
    public static List<string> Unapplied(IEnumerable<PlatformStep> published, Assembly migrations)
    {
        var applied = migrations.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => ((Migration)Activator.CreateInstance(t)!).UpOperations.OfType<SqlOperation>())
            .Select(o => o.Sql)
            .ToList();
        return published.Select(s => s.Name)
            .Where(name => !applied.Any(sql => sql.StartsWith(PlatformSteps.Marker(name), StringComparison.Ordinal)))
            .ToList();
    }
}
