using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Perezosoft.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 🗑️ DELETE-ME with the Notes sample. The sample's RLS policy, in the sample's own migration (Arch A6, #366): until
    /// now <c>RlsTenancyBackstop</c> named the <c>Notes</c> table in its frozen list, so an app removing the sample had to
    /// edit a platform migration (vuelto did, and its platform migrations diverged from upstream). The sample's unit is
    /// now <c>AddNotesSample</c> (the table) plus this file (its policy); no platform migration names it.
    /// <para>
    /// Frozen output of <c>RlsDdl.StatementsFor("Notes", "TenantId")</c>, idempotent: a database that already carries the
    /// policy from the backstop's earlier run is unchanged; a fresh database gets it here. Data loss on Down: none —
    /// the policy and the forced RLS are removed, the rows stay.
    /// </para>
    /// <para>
    /// Removing the sample from an app: the recipe in <c>docs/NEW_APP_GUIDE.md</c> ("Removing the Notes sample") adds an
    /// app migration that drops the table and leaves this file and <c>AddNotesSample</c> as history — never delete or
    /// edit a platform migration.
    /// </para>
    /// </summary>
    public partial class NotesSampleRlsPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Notes" ENABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""ALTER TABLE "Notes" FORCE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY IF EXISTS rls_tenant_isolation ON "Notes";""");
            migrationBuilder.Sql(
                """
                CREATE POLICY rls_tenant_isolation ON "Notes"
                    AS PERMISSIVE FOR ALL
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                            OR current_setting('app.rls_bypass', true) = 'on')
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                            OR current_setting('app.rls_bypass', true) = 'on');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS rls_tenant_isolation ON "Notes";""");
            migrationBuilder.Sql("""ALTER TABLE "Notes" NO FORCE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""ALTER TABLE "Notes" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
