using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProductionWorkerPlatform : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "domain",
            table: "jobs",
            type: "character varying(253)",
            maxLength: 253,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_failure_kind",
            table: "jobs",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_heartbeat_at",
            table: "jobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "pause_requested_at",
            table: "jobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "paused_at",
            table: "jobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "recovery_count",
            table: "jobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "domain_rate_limits",
            columns: table => new
            {
                scope_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                next_allowed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_domain_rate_limits", x => new { x.scope_key, x.domain });
                table.CheckConstraint("ck_domain_rate_limit_domain", "char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain)");
                table.ForeignKey(
                    name: "FK_domain_rate_limits_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "worker_heartbeats",
            columns: table => new
            {
                worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                machine_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                process_id = table.Column<int>(type: "integer", nullable: false),
                concurrency = table.Column<int>(type: "integer", nullable: false),
                buffer_size = table.Column<int>(type: "integer", nullable: false),
                active_jobs = table.Column<int>(type: "integer", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_worker_heartbeats", x => x.worker_id);
                table.CheckConstraint("ck_worker_heartbeat_active", "active_jobs BETWEEN 0 AND concurrency");
                table.CheckConstraint("ck_worker_heartbeat_buffer", "buffer_size BETWEEN 1 AND 1000");
                table.CheckConstraint("ck_worker_heartbeat_concurrency", "concurrency BETWEEN 1 AND 32");
            });

        migrationBuilder.CreateIndex(
            name: "IX_jobs_campaign_id_status_claim_expires_at",
            table: "jobs",
            columns: new[] { "campaign_id", "status", "claim_expires_at" });

        migrationBuilder.CreateIndex(
            name: "IX_jobs_domain_status_claim_expires_at",
            table: "jobs",
            columns: new[] { "domain", "status", "claim_expires_at" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_jobs_domain",
            table: "jobs",
            sql: "domain IS NULL OR (char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_jobs_recovery_count",
            table: "jobs",
            sql: "recovery_count >= 0");

        migrationBuilder.CreateIndex(
            name: "IX_domain_rate_limits_next_allowed_at",
            table: "domain_rate_limits",
            column: "next_allowed_at");

        migrationBuilder.CreateIndex(
            name: "IX_domain_rate_limits_project_id",
            table: "domain_rate_limits",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "IX_worker_heartbeats_stopped_at_last_heartbeat_at",
            table: "worker_heartbeats",
            columns: new[] { "stopped_at", "last_heartbeat_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "domain_rate_limits");

        migrationBuilder.DropTable(
            name: "worker_heartbeats");

        migrationBuilder.DropIndex(
            name: "IX_jobs_campaign_id_status_claim_expires_at",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "IX_jobs_domain_status_claim_expires_at",
            table: "jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_jobs_domain",
            table: "jobs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_jobs_recovery_count",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "domain",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "last_failure_kind",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "last_heartbeat_at",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "pause_requested_at",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "paused_at",
            table: "jobs");

        migrationBuilder.DropColumn(
            name: "recovery_count",
            table: "jobs");
    }
}
