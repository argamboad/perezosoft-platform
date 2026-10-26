using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppBrand.Host.Persistence.Migrations
{
    /// <summary>
    /// 🗑️ DELETE-ME with the Notes sample: the sample table's row-level-security policy, the same fail-closed tenant
    /// policy the platform's tables carry. A tenant-scoped table of your own gets one like it in its own migration.
    /// <para>
    /// Removing the sample: add a migration that drops the <c>Notes</c> table and leave this file and
    /// <c>InitialCreate</c> as history — never edit an applied migration.
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
