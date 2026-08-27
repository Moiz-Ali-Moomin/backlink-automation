using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBacklinkProStyleWorkflow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "credential_reference",
            table: "wordpress_site_profiles",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.CreateTable(
            name: "backlink_workflows",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_import_id = table.Column<Guid>(type: "uuid", nullable: true),
                identity_pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                template_pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                global_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_delay_milliseconds = table.Column<int>(type: "integer", nullable: false),
                maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                verification_delay_seconds = table.Column<int>(type: "integer", nullable: false),
                campaign_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                orchestration_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_backlink_workflows", x => x.id);
                table.CheckConstraint("ck_backlink_workflow_attempts", "maximum_attempts BETWEEN 1 AND 20");
                table.CheckConstraint("ck_backlink_workflow_campaign_limit", "cardinality(campaign_ids) <= 1000");
                table.CheckConstraint("ck_backlink_workflow_concurrency", "global_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND global_concurrency");
                table.CheckConstraint("ck_backlink_workflow_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
                table.CheckConstraint("ck_backlink_workflow_verification_delay", "verification_delay_seconds BETWEEN 0 AND 2592000");
                table.ForeignKey(
                    name: "FK_backlink_workflows_jobs_orchestration_job_id",
                    column: x => x.orchestration_job_id,
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_backlink_workflows_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_backlink_workflows_submission_identity_pools_identity_pool_~",
                    column: x => x.identity_pool_id,
                    principalTable: "submission_identity_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_backlink_workflows_submission_source_imports_source_import_~",
                    column: x => x.source_import_id,
                    principalTable: "submission_source_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_backlink_workflows_submission_template_pools_template_pool_~",
                    column: x => x.template_pool_id,
                    principalTable: "submission_template_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "backlink_workflow_sources",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                submission_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_backlink_workflow_sources", x => x.id);
                table.ForeignKey(
                    name: "FK_backlink_workflow_sources_backlink_workflows_workflow_id",
                    column: x => x.workflow_id,
                    principalTable: "backlink_workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_backlink_workflow_sources_campaigns_campaign_id",
                    column: x => x.campaign_id,
                    principalTable: "campaigns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_backlink_workflow_sources_submission_sources_submission_sou~",
                    column: x => x.submission_source_id,
                    principalTable: "submission_sources",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflow_sources_campaign_id",
            table: "backlink_workflow_sources",
            column: "campaign_id");

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflow_sources_submission_source_id",
            table: "backlink_workflow_sources",
            column: "submission_source_id");

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflow_sources_workflow_id_created_at_id",
            table: "backlink_workflow_sources",
            columns: new[] { "workflow_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflow_sources_workflow_id_normalized_url",
            table: "backlink_workflow_sources",
            columns: new[] { "workflow_id", "normalized_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflows_identity_pool_id",
            table: "backlink_workflows",
            column: "identity_pool_id");

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflows_orchestration_job_id",
            table: "backlink_workflows",
            column: "orchestration_job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflows_project_id_created_at_id",
            table: "backlink_workflows",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflows_source_import_id",
            table: "backlink_workflows",
            column: "source_import_id");

        migrationBuilder.CreateIndex(
            name: "IX_backlink_workflows_template_pool_id",
            table: "backlink_workflows",
            column: "template_pool_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backlink_workflow_sources");

        migrationBuilder.DropTable(
            name: "backlink_workflows");

        migrationBuilder.AlterColumn<string>(
            name: "credential_reference",
            table: "wordpress_site_profiles",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);
    }
}
