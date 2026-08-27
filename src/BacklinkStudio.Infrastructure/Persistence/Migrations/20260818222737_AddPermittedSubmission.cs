using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BacklinkStudioDbContext))]
[Migration("20260818222737_AddPermittedSubmission")]
public sealed class AddPermittedSubmission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "approved_at",
            table: "opportunities",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "approved_by",
            table: "opportunities",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "campaigns",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                approval_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                authorization_profile_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                authorization_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                daily_action_limit = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaigns", x => x.id);
                table.CheckConstraint("ck_campaign_daily_limit", "daily_action_limit BETWEEN 1 AND 100000");
                table.ForeignKey("FK_campaigns_projects_project_id", x => x.project_id, "projects", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "campaign_opportunities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                explicitly_approved = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaign_opportunities", x => x.id);
                table.ForeignKey("FK_campaign_opportunities_campaigns_campaign_id", x => x.campaign_id, "campaigns", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_campaign_opportunities_opportunities_opportunity_id", x => x.opportunity_id, "opportunities", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "campaign_targets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaign_targets", x => x.id);
                table.ForeignKey("FK_campaign_targets_campaigns_campaign_id", x => x.campaign_id, "campaigns", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_campaign_targets_project_targets_project_target_id", x => x.project_target_id, "project_targets", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "submission_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                persistent_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_jobs", x => x.id);
                table.ForeignKey("FK_submission_jobs_campaign_opportunities_campaign_opportunity_id", x => x.campaign_opportunity_id, "campaign_opportunities", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_submission_jobs_campaigns_campaign_id", x => x.campaign_id, "campaigns", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_submission_jobs_jobs_persistent_job_id", x => x.persistent_job_id, "jobs", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_submission_jobs_projects_project_id", x => x.project_id, "projects", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                submission_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt_number = table.Column<int>(type: "integer", nullable: false),
                adapter = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                http_status = table.Column<int>(type: "integer", nullable: true),
                external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_attempts", x => x.id);
                table.CheckConstraint("ck_submission_attempt_number", "attempt_number > 0");
                table.ForeignKey("FK_submission_attempts_submission_jobs_submission_job_id", x => x.submission_job_id, "submission_jobs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_campaign_opportunities_campaign_id_opportunity_id", "campaign_opportunities", new[] { "campaign_id", "opportunity_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_campaign_opportunities_opportunity_id", "campaign_opportunities", "opportunity_id");
        migrationBuilder.CreateIndex("IX_campaign_targets_campaign_id_project_target_id", "campaign_targets", new[] { "campaign_id", "project_target_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_campaign_targets_project_target_id", "campaign_targets", "project_target_id");
        migrationBuilder.CreateIndex("IX_campaigns_project_id_created_at_id", "campaigns", new[] { "project_id", "created_at", "id" });
        migrationBuilder.CreateIndex("IX_submission_attempts_submission_job_id_attempt_number", "submission_attempts", new[] { "submission_job_id", "attempt_number" }, unique: true);
        migrationBuilder.CreateIndex("IX_submission_jobs_campaign_id_created_at_id", "submission_jobs", new[] { "campaign_id", "created_at", "id" });
        migrationBuilder.CreateIndex("IX_submission_jobs_campaign_opportunity_id", "submission_jobs", "campaign_opportunity_id", unique: true);
        migrationBuilder.CreateIndex("IX_submission_jobs_persistent_job_id", "submission_jobs", "persistent_job_id", unique: true);
        migrationBuilder.CreateIndex("IX_submission_jobs_project_id", "submission_jobs", "project_id");
        migrationBuilder.AddForeignKey("FK_jobs_campaigns_campaign_id", "jobs", "campaign_id", "campaigns", principalColumn: "id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_jobs_campaigns_campaign_id", "jobs");
        migrationBuilder.DropTable("campaign_targets");
        migrationBuilder.DropTable("submission_attempts");
        migrationBuilder.DropTable("submission_jobs");
        migrationBuilder.DropTable("campaign_opportunities");
        migrationBuilder.DropTable("campaigns");
        migrationBuilder.DropColumn("approved_at", "opportunities");
        migrationBuilder.DropColumn("approved_by", "opportunities");
    }
}
