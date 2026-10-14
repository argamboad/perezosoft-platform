using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Perezosoft.Infrastructure.Persistence;

/// <summary>
/// One published platform step (Pkg 3.6, ADR-034 §5): SQL that EF cannot scaffold from a model change — a row-level
/// security policy, a data step — which an app's migration applies by name with
/// <see cref="PlatformStepMigrationExtensions.ApplyPlatformStep"/>. A published step is immutable: a migration that
/// already ran applied its SQL as it was, so a change ships as a new step under a new name.
/// </summary>
public sealed class PlatformStep(string name, string summary, IReadOnlyList<string> up, IReadOnlyList<string> down)
{
    /// <summary>The name a migration and a release note use for it.</summary>
    public string Name { get; } = name;

    /// <summary>One line on what it does, for the release note.</summary>
    public string Summary { get; } = summary;

    /// <summary>The statements a migration's <c>Up</c> runs.</summary>
    public IReadOnlyList<string> Up { get; } = up;

    /// <summary>The statements a migration's <c>Down</c> runs.</summary>
    public IReadOnlyList<string> Down { get; } = down;

    /// <summary>
    /// SHA-256 of what the step emits, line endings normalized (a Windows checkout's literals carry CRLF, CI's LF; the
    /// SQL means the same). Compared with <see cref="PlatformSteps.RecordedFingerprints"/> by the platform's tests.
    /// </summary>
    public string Fingerprint()
    {
        var text = string.Join("\n", Up) + "\n-- down --\n" + string.Join("\n", Down);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));
    }
}

/// <summary>
/// The platform's published steps (Pkg 3.6). A package release that adds one names it in its release note; the app's
/// next migration calls <c>migrationBuilder.ApplyPlatformStep(name)</c>, and <c>PlatformStepTests</c> fails until some
/// migration in the app does. Steps are only ever added, never edited or removed (platform model changes are additive).
/// </summary>
public static class PlatformSteps
{
    /// <summary>ADR-020's row-level-security backstop on the platform's tenant-scoped tables.</summary>
    public const string RlsTenancyBackstop = "RlsTenancyBackstop";

    // The platform's ITenantScoped tables as of 2026-07-06, frozen — minus the DELETE-ME sample's (Arch A6, 2026-10-07):
    // "Notes" left this list so that no platform migration names the sample; its policy is the sample's own migration
    // (NotesSampleRlsPolicy). A later ITenantScoped platform table ships in a NEW step, never by growing this one.
    private static readonly string[] BackstopTables =
        ["ApiKeys", "AuditEvents", "Subscriptions", "TenantInvitations", "UsageCounters", "WebhookSubscriptions"];

    /// <summary>Every published step, in publication order.</summary>
    public static IReadOnlyList<PlatformStep> Published { get; } =
    [
        new(RlsTenancyBackstop,
            "Enables and forces row-level security with the fail-closed tenant policy on the platform's tenant-scoped tables (ADR-020).",
            [.. BackstopTables.SelectMany(t => RlsDdl.StatementsFor(t, "TenantId"))],
            [.. BackstopTables.SelectMany(RlsDdl.DropStatementsFor)]),
    ];

    /// <summary>
    /// What each published step emitted when it was published. A step whose fingerprint drifts from this (a change in
    /// <see cref="RlsDdl"/>, say) fails the platform's tests: publish a new step instead.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RecordedFingerprints { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RlsTenancyBackstop] = "7521801d9369af669e818992ee65ba70c4b9d9242bd0b0d2b6fc66b671de5cc7",
    };

    /// <summary>The published step of that name; an unknown name fails the migration that asked for it.</summary>
    public static PlatformStep Get(string name) =>
        Published.FirstOrDefault(s => s.Name == name)
        ?? throw new InvalidOperationException(
            $"no published platform step named '{name}' — the package this app references does not publish it (bump the package?). "
            + $"Published: {string.Join(", ", Published.Select(s => s.Name))}");

    /// <summary>The comment that opens a step's first statement, so the migration script and the gate can see which step ran.</summary>
    public static string Marker(string name) => $"/* platform-step: {name} */";
}

/// <summary>The calls a migration makes (Pkg 3.6): <c>migrationBuilder.ApplyPlatformStep(PlatformSteps.RlsTenancyBackstop)</c>.</summary>
public static class PlatformStepMigrationExtensions
{
    /// <summary>Runs the step's <c>Up</c> statements; the first carries <see cref="PlatformSteps.Marker"/>.</summary>
    public static MigrationBuilder ApplyPlatformStep(this MigrationBuilder migrationBuilder, string name)
    {
        var step = PlatformSteps.Get(name);
        for (var i = 0; i < step.Up.Count; i++)
            migrationBuilder.Sql(i == 0 ? $"{PlatformSteps.Marker(step.Name)}\n{step.Up[i]}" : step.Up[i]);
        return migrationBuilder;
    }

    /// <summary>Runs the step's <c>Down</c> statements, for a migration's <c>Down</c>.</summary>
    public static MigrationBuilder RevertPlatformStep(this MigrationBuilder migrationBuilder, string name)
    {
        foreach (var sql in PlatformSteps.Get(name).Down) migrationBuilder.Sql(sql);
        return migrationBuilder;
    }
}
