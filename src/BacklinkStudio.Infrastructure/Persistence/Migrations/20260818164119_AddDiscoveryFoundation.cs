using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDiscoveryFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "discovery_queries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                query_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                input_json = table.Column<string>(type: "jsonb", nullable: false),
                maximum_results = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_discovery_queries", x => x.id);
                table.CheckConstraint("ck_discovery_query_maximum_results", "maximum_results BETWEEN 1 AND 5000");
                table.ForeignKey(
                    name: "FK_discovery_queries_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "discovery_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                discovery_query_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                urls_discovered = table.Column<int>(type: "integer", nullable: false),
                urls_accepted = table.Column<int>(type: "integer", nullable: false),
                duplicate_count = table.Column<int>(type: "integer", nullable: false),
                blocked_count = table.Column<int>(type: "integer", nullable: false),
                invalid_count = table.Column<int>(type: "integer", nullable: false),
                error_count = table.Column<int>(type: "integer", nullable: false),
                errors = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_discovery_runs", x => x.id);
                table.CheckConstraint("ck_discovery_run_accepted", "urls_accepted >= 0");
                table.CheckConstraint("ck_discovery_run_blocked", "blocked_count >= 0");
                table.CheckConstraint("ck_discovery_run_discovered", "urls_discovered >= 0");
                table.CheckConstraint("ck_discovery_run_duplicates", "duplicate_count >= 0");
                table.CheckConstraint("ck_discovery_run_errors", "error_count >= 0");
                table.CheckConstraint("ck_discovery_run_invalid", "invalid_count >= 0");
                table.ForeignKey(
                    name: "FK_discovery_runs_discovery_queries_discovery_query_id",
                    column: x => x.discovery_query_id,
                    principalTable: "discovery_queries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_discovery_runs_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_discovery_runs_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_discovery_queries_project_id_created_at_id",
            table: "discovery_queries",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_discovery_runs_discovery_query_id",
            table: "discovery_runs",
            column: "discovery_query_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_discovery_runs_job_id",
            table: "discovery_runs",
            column: "job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_discovery_runs_project_id_created_at_id",
            table: "discovery_runs",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_discovery_runs_project_id_status",
            table: "discovery_runs",
            columns: new[] { "project_id", "status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "discovery_runs");

        migrationBuilder.DropTable(
            name: "discovery_queries");
    }
}
