using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Perezosoft.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Pkg 5.8 (#451): the model now says what MfaLockout's AddColumn(defaultValue: 0) already put in the database — a
    /// default of 0 on UserMfa.FailedAttemptCount — so an app that builds the platform's tables from the model gets the
    /// column platform-schema.json records. Where MfaLockout ran, Up re-states the default (no change); Down leaves it,
    /// because it was there before this migration and stays MfaLockout's to remove.
    /// </summary>
    public partial class UserMfaFailedAttemptCountDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FailedAttemptCount",
                table: "UserMfa",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing: the default predates this migration (MfaLockout's AddColumn); dropping it here would leave a database
            // that ran MfaLockout without the default MfaLockout gave it.
        }
    }
}
