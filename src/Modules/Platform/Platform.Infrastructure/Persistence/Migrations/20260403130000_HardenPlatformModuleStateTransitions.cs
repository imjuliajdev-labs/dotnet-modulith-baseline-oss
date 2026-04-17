using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class HardenPlatformModuleStateTransitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DesiredState",
            schema: "platform",
            table: "module_states",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastErrorCode",
            schema: "platform",
            table: "module_states",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastErrorDetail",
            schema: "platform",
            table: "module_states",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TransitionId",
            schema: "platform",
            table: "module_states",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UpdatedByActorId",
            schema: "platform",
            table: "module_states",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedUtc",
            schema: "platform",
            table: "module_states",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "platform",
            table: "module_states",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<Guid>(
            name: "TransitionId",
            schema: "platform",
            table: "module_state_changes",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE platform.module_states
            SET "DesiredState" = CASE
                WHEN "RuntimeState" IN ('Enabled', 'Enabling') THEN 'Enabled'
                ELSE 'Disabled'
            END,
                "UpdatedUtc" = CURRENT_TIMESTAMP
            """);

        migrationBuilder.AlterColumn<string>(
            name: "DesiredState",
            schema: "platform",
            table: "module_states",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "UpdatedUtc",
            schema: "platform",
            table: "module_states",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DesiredState",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "LastErrorCode",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "LastErrorDetail",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "TransitionId",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "UpdatedByActorId",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "UpdatedUtc",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "Version",
            schema: "platform",
            table: "module_states");

        migrationBuilder.DropColumn(
            name: "TransitionId",
            schema: "platform",
            table: "module_state_changes");
    }
}
