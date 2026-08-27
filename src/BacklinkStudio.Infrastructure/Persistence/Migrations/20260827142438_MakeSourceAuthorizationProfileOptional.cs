using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MakeSourceAuthorizationProfileOptional : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_submission_sources_automation_ownership",
            table: "submission_sources");

        migrationBuilder.AlterColumn<Guid>(
            name: "owned_network_profile_id",
            table: "submission_sources",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "owned_network_profile_id",
            table: "submission_source_imports",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddCheckConstraint(
            name: "ck_submission_sources_automation_ownership",
            table: "submission_sources",
            sql: "NOT automation_permitted OR (owned_network_profile_id IS NOT NULL AND ownership_status <> 'Unverified')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_submission_sources_automation_ownership",
            table: "submission_sources");

        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM submission_sources WHERE owned_network_profile_id IS NULL)
                   OR EXISTS (SELECT 1 FROM submission_source_imports WHERE owned_network_profile_id IS NULL) THEN
                    RAISE EXCEPTION 'Cannot restore required owned-network profile columns while profile-free imports exist.';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "owned_network_profile_id",
            table: "submission_sources",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "owned_network_profile_id",
            table: "submission_source_imports",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_submission_sources_automation_ownership",
            table: "submission_sources",
            sql: "NOT automation_permitted OR ownership_status <> 'Unverified'");
    }
}
