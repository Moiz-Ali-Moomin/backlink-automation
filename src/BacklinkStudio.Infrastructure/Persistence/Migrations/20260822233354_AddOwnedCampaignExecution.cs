using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOwnedCampaignExecution : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_campaign_opportunity_id",
            table: "submission_jobs");

        migrationBuilder.AlterColumn<Guid>(
            name: "campaign_opportunity_id",
            table: "submission_jobs",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<string>(
            name: "placement_type",
            table: "submission_jobs",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "submission_source_id",
            table: "submission_jobs",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "target_url",
            table: "submission_jobs",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "campaign_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "failure_kind",
            table: "submission_attempts",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "None");

        migrationBuilder.AddColumn<Guid>(
            name: "identity_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "moderation_status",
            table: "submission_attempts",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<Guid>(
            name: "persistent_job_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "project_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "resolved_display_name",
            table: "submission_attempts",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "resolved_email",
            table: "submission_attempts",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "resolved_website",
            table: "submission_attempts",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "strategy",
            table: "submission_attempts",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "submission_source_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "target_url",
            table: "submission_attempts",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "template_id",
            table: "submission_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "owned_network_campaign_configurations",
            columns: table => new
            {
                campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                owned_network_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                identity_pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                template_pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                global_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_delay_milliseconds = table.Column<int>(type: "integer", nullable: false),
                maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                verification_delay_seconds = table.Column<int>(type: "integer", nullable: false),
                mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                cms_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                technical_compatibility = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                validation_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                last_source_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                sources_queued = table.Column<long>(type: "bigint", nullable: false),
                expansion_completed = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_owned_network_campaign_configurations", x => x.campaign_id);
                table.CheckConstraint("ck_owned_campaign_attempts", "maximum_attempts BETWEEN 1 AND 20");
                table.CheckConstraint("ck_owned_campaign_concurrency", "global_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND global_concurrency");
                table.CheckConstraint("ck_owned_campaign_domain_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
                table.CheckConstraint("ck_owned_campaign_sources_queued", "sources_queued >= 0");
                table.CheckConstraint("ck_owned_campaign_verification_delay", "verification_delay_seconds BETWEEN 0 AND 2592000");
                table.ForeignKey(
                    name: "FK_owned_network_campaign_configurations_campaigns_campaign_id",
                    column: x => x.campaign_id,
                    principalTable: "campaigns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_owned_network_campaign_configurations_owned_network_profile~",
                    column: x => x.owned_network_profile_id,
                    principalTable: "owned_network_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_owned_network_campaign_configurations_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_owned_network_campaign_configurations_submission_identity_p~",
                    column: x => x.identity_pool_id,
                    principalTable: "submission_identity_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_owned_network_campaign_configurations_submission_template_p~",
                    column: x => x.template_pool_id,
                    principalTable: "submission_template_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_campaign_id_submission_source_id_target_url~",
            table: "submission_jobs",
            columns: new[] { "campaign_id", "submission_source_id", "target_url", "placement_type" },
            unique: true,
            filter: "submission_source_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_campaign_opportunity_id",
            table: "submission_jobs",
            column: "campaign_opportunity_id",
            unique: true,
            filter: "campaign_opportunity_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_submission_source_id",
            table: "submission_jobs",
            column: "submission_source_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_attempts_identity_id",
            table: "submission_attempts",
            column: "identity_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_attempts_persistent_job_id",
            table: "submission_attempts",
            column: "persistent_job_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_attempts_project_id_campaign_id_started_at",
            table: "submission_attempts",
            columns: new[] { "project_id", "campaign_id", "started_at" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_attempts_submission_source_id",
            table: "submission_attempts",
            column: "submission_source_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_attempts_template_id",
            table: "submission_attempts",
            column: "template_id");

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_campaign_configurations_identity_pool_id",
            table: "owned_network_campaign_configurations",
            column: "identity_pool_id");

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_campaign_configurations_owned_network_profile~",
            table: "owned_network_campaign_configurations",
            columns: new[] { "owned_network_profile_id", "expansion_completed", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_campaign_configurations_project_id",
            table: "owned_network_campaign_configurations",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_campaign_configurations_template_pool_id",
            table: "owned_network_campaign_configurations",
            column: "template_pool_id");

        migrationBuilder.AddForeignKey(
            name: "FK_submission_attempts_jobs_persistent_job_id",
            table: "submission_attempts",
            column: "persistent_job_id",
            principalTable: "jobs",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_submission_attempts_submission_identities_identity_id",
            table: "submission_attempts",
            column: "identity_id",
            principalTable: "submission_identities",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_submission_attempts_submission_sources_submission_source_id",
            table: "submission_attempts",
            column: "submission_source_id",
            principalTable: "submission_sources",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_submission_attempts_submission_templates_template_id",
            table: "submission_attempts",
            column: "template_id",
            principalTable: "submission_templates",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_submission_jobs_submission_sources_submission_source_id",
            table: "submission_jobs",
            column: "submission_source_id",
            principalTable: "submission_sources",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_submission_attempts_jobs_persistent_job_id",
            table: "submission_attempts");

        migrationBuilder.DropForeignKey(
            name: "FK_submission_attempts_submission_identities_identity_id",
            table: "submission_attempts");

        migrationBuilder.DropForeignKey(
            name: "FK_submission_attempts_submission_sources_submission_source_id",
            table: "submission_attempts");

        migrationBuilder.DropForeignKey(
            name: "FK_submission_attempts_submission_templates_template_id",
            table: "submission_attempts");

        migrationBuilder.DropForeignKey(
            name: "FK_submission_jobs_submission_sources_submission_source_id",
            table: "submission_jobs");

        migrationBuilder.DropTable(
            name: "owned_network_campaign_configurations");

        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_campaign_id_submission_source_id_target_url~",
            table: "submission_jobs");

        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_campaign_opportunity_id",
            table: "submission_jobs");

        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_submission_source_id",
            table: "submission_jobs");

        migrationBuilder.DropIndex(
            name: "IX_submission_attempts_identity_id",
            table: "submission_attempts");

        migrationBuilder.DropIndex(
            name: "IX_submission_attempts_persistent_job_id",
            table: "submission_attempts");

        migrationBuilder.DropIndex(
            name: "IX_submission_attempts_project_id_campaign_id_started_at",
            table: "submission_attempts");

        migrationBuilder.DropIndex(
            name: "IX_submission_attempts_submission_source_id",
            table: "submission_attempts");

        migrationBuilder.DropIndex(
            name: "IX_submission_attempts_template_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "placement_type",
            table: "submission_jobs");

        migrationBuilder.DropColumn(
            name: "submission_source_id",
            table: "submission_jobs");

        migrationBuilder.DropColumn(
            name: "target_url",
            table: "submission_jobs");

        migrationBuilder.DropColumn(
            name: "campaign_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "failure_kind",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "identity_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "moderation_status",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "persistent_job_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "project_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "resolved_display_name",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "resolved_email",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "resolved_website",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "strategy",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "submission_source_id",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "target_url",
            table: "submission_attempts");

        migrationBuilder.DropColumn(
            name: "template_id",
            table: "submission_attempts");

        migrationBuilder.AlterColumn<Guid>(
            name: "campaign_opportunity_id",
            table: "submission_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_campaign_opportunity_id",
            table: "submission_jobs",
            column: "campaign_opportunity_id",
            unique: true);
    }
}
