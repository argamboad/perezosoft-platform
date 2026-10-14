using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Perezosoft.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-020: row-level security as the DB-level second tenancy wall. Enables + FORCEs RLS and
    /// creates the fail-closed tenant-isolation policy on every ITenantScoped table that exists at
    /// this point (frozen list — a future ITenantScoped entity ships its own policy migration; the
    /// RlsMigrationGateTests parity gate fails CI if one is forgotten).
    /// <para>
    /// The SQL is the published platform step <see cref="PlatformSteps.RlsTenancyBackstop"/> (Pkg 3.6): the frozen table
    /// list and the frozen output of RlsDdl for it live in the package, which never changes a published step
    /// (PlatformStepTests holds its fingerprint). Before the step, this file carried the same statements inline. The
    /// sample's "Notes" is not in the list (Arch A6): its policy is the sample's own NotesSampleRlsPolicy migration.
    /// </para>
    /// <para>
    /// Enforcement is role-dependent by design: superusers and BYPASSRLS roles (local dev, CI
    /// containers) bypass RLS entirely; a non-superuser connection — including a table OWNER, which
    /// is what FORCE is for (e.g. Neon's database owner) — is subject to the policy, with the
    /// tenant/bypass GUCs set per command by RlsSessionInterceptor. Roles and grants are environment
    /// provisioning (docker/db/provision-rls-runtime-role.sql, DEPLOYMENT.md), not schema —
    /// deliberately not in this migration.
    /// </para>
    /// </summary>
    public partial class RlsTenancyBackstop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.ApplyPlatformStep(PlatformSteps.RlsTenancyBackstop);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RevertPlatformStep(PlatformSteps.RlsTenancyBackstop);
        }
    }
}
