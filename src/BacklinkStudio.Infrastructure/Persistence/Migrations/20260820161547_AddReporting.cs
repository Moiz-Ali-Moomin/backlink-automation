using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddReporting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "reports",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                job_id = table.Column<Guid>(type: "uuid", nullable: true),
                kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                artifact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                byte_length = table.Column<long>(type: "bigint", nullable: true),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                row_count = table.Column<int>(type: "integer", nullable: true),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reports", x => x.id);
                table.CheckConstraint("ck_reports_byte_length", "byte_length IS NULL OR byte_length >= 0");
                table.CheckConstraint("ck_reports_row_count", "row_count IS NULL OR row_count >= 0");
                table.ForeignKey(
                    name: "FK_reports_campaigns_campaign_id",
                    column: x => x.campaign_id,
                    principalTable: "campaigns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_reports_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_reports_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_reports_campaign_id_created_at_id",
            table: "reports",
            columns: new[] { "campaign_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_reports_job_id",
            table: "reports",
            column: "job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_reports_project_id_created_at_id",
            table: "reports",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_reports_status_created_at",
            table: "reports",
            columns: new[] { "status", "created_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reports");
    }
}
