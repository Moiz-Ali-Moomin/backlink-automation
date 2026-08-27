using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBacklinkVerification : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "backlinks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                submission_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                anchor_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                rel = table.Column<string[]>(type: "text[]", nullable: false),
                nofollow = table.Column<bool>(type: "boolean", nullable: false),
                ugc = table.Column<bool>(type: "boolean", nullable: false),
                sponsored = table.Column<bool>(type: "boolean", nullable: false),
                http_status = table.Column<int>(type: "integer", nullable: true),
                canonical_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_backlinks", x => x.id);
                table.ForeignKey(
                    name: "FK_backlinks_campaigns_campaign_id",
                    column: x => x.campaign_id,
                    principalTable: "campaigns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_backlinks_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_backlinks_submission_jobs_submission_job_id",
                    column: x => x.submission_job_id,
                    principalTable: "submission_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "verification_checks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                backlink_id = table.Column<Guid>(type: "uuid", nullable: false),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                found = table.Column<bool>(type: "boolean", nullable: false),
                http_status = table.Column<int>(type: "integer", nullable: true),
                anchor = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                rel = table.Column<string[]>(type: "text[]", nullable: false),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                duration_milliseconds = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_verification_checks", x => x.id);
                table.CheckConstraint("ck_verification_duration", "duration_milliseconds >= 0");
                table.ForeignKey(
                    name: "FK_verification_checks_backlinks_backlink_id",
                    column: x => x.backlink_id,
                    principalTable: "backlinks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_campaign_id",
            table: "backlinks",
            column: "campaign_id");

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_domain_status",
            table: "backlinks",
            columns: new[] { "domain", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_last_checked_at",
            table: "backlinks",
            column: "last_checked_at");

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_project_id_normalized_source_url_normalized_targe~",
            table: "backlinks",
            columns: new[] { "project_id", "normalized_source_url", "normalized_target_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_project_id_status_created_at_id",
            table: "backlinks",
            columns: new[] { "project_id", "status", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_submission_job_id",
            table: "backlinks",
            column: "submission_job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_verification_checks_backlink_id_checked_at_id",
            table: "verification_checks",
            columns: new[] { "backlink_id", "checked_at", "id" });

        migrationBuilder.Sql(
            """
            CREATE FUNCTION backlinkstudio_reject_verification_check_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                RAISE EXCEPTION 'verification_checks is append-only' USING ERRCODE = '55000';
            END;
            $function$;

            CREATE TRIGGER verification_checks_append_only
            BEFORE UPDATE OR DELETE ON verification_checks
            FOR EACH ROW
            EXECUTE FUNCTION backlinkstudio_reject_verification_check_mutation();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS verification_checks_append_only ON verification_checks;
            DROP FUNCTION IF EXISTS backlinkstudio_reject_verification_check_mutation();
            """);

        migrationBuilder.DropTable(
            name: "verification_checks");

        migrationBuilder.DropTable(
            name: "backlinks");
    }
}
