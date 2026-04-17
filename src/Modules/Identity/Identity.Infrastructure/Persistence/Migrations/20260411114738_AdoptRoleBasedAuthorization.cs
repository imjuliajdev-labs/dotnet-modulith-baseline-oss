using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdoptRoleBasedAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 3: drop legacy permission plumbing created by AdoptAspNetCoreIdentityEfCore.
            // Triggers/functions referenced accounts.permissions; they must be torn down before the
            // underlying columns can be dropped. Permission claim rows are also purged so the
            // account_claims table is reused solely for the built-in Identity claim model.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_permission_claims_to_legacy_permissions ON identity.account_claims;
                DROP TRIGGER IF EXISTS trg_sync_legacy_permissions_to_permission_claims ON identity.accounts;
                DROP FUNCTION IF EXISTS identity.trg_sync_permission_claims_to_legacy_permissions();
                DROP FUNCTION IF EXISTS identity.trg_sync_legacy_permissions_to_permission_claims();
                DROP FUNCTION IF EXISTS identity.sync_permission_claims_to_legacy_permissions(TEXT);
                DROP FUNCTION IF EXISTS identity.sync_legacy_permissions_to_permission_claims(TEXT);

                DELETE FROM identity.account_claims WHERE claim_type = 'permission';

                ALTER TABLE identity.accounts DROP COLUMN IF EXISTS permissions;
                """);

            migrationBuilder.DropColumn(
                name: "permission_snapshot_version",
                schema: "identity",
                table: "machine_clients");

            migrationBuilder.DropColumn(
                name: "permission_snapshot_version",
                schema: "identity",
                table: "accounts");

            migrationBuilder.RenameColumn(
                name: "permissions",
                schema: "identity",
                table: "machine_clients",
                newName: "roles");

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_roles",
                schema: "identity",
                columns: table => new
                {
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_roles", x => new { x.actor_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_account_roles_accounts_actor_id",
                        column: x => x.actor_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "actor_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_account_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_roles_role_id",
                schema: "identity",
                table: "account_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_role_id",
                schema: "identity",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "roles",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.RenameColumn(
                name: "roles",
                schema: "identity",
                table: "machine_clients",
                newName: "permissions");

            migrationBuilder.AddColumn<string>(
                name: "permission_snapshot_version",
                schema: "identity",
                table: "machine_clients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "permission_snapshot_version",
                schema: "identity",
                table: "accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}
