using Microsoft.EntityFrameworkCore.Migrations;
using Perezosoft.Infrastructure.Persistence;

#nullable disable

namespace AppBrand.Host.Persistence.Migrations
{
    /// <summary>
    /// The platform's row-level-security backstop on its tenant-scoped tables (ADR-020), as a published platform step:
    /// the SQL is the package's, so this file never changes. A later platform release that publishes a new step says so in
    /// its release notes, and the platform's tests fail until a migration of this app applies it.
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
