using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAnalysisPolicyFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "analysis_error",
            table: "candidate_pages",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "canonical_url",
            table: "candidate_pages",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cms",
            table: "candidate_pages",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "content_type",
            table: "candidate_pages",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "eligible_signals",
            table: "candidate_pages",
            type: "text[]",
            nullable: false,
            defaultValue: Array.Empty<string>());

        migrationBuilder.AddColumn<bool>(
            name: "existing_target_link",
            table: "candidate_pages",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "final_url",
            table: "candidate_pages",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "http_status",
            table: "candidate_pages",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "requires_javascript",
            table: "candidate_pages",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "robots_directives",
            table: "candidate_pages",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "title",
            table: "candidate_pages",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "blocklist_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                match_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                value = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_blocklist_entries", x => x.id);
                table.ForeignKey(
                    name: "FK_blocklist_entries_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "opportunity_score_reasons",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                points = table.Column<int>(type: "integer", nullable: false),
                explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opportunity_score_reasons", x => x.id);
                table.ForeignKey(
                    name: "FK_opportunity_score_reasons_opportunities_opportunity_id",
                    column: x => x.opportunity_id,
                    principalTable: "opportunities",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "policy_definitions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                automation_enabled = table.Column<bool>(type: "boolean", nullable: false),
                minimum_quality_score = table.Column<int>(type: "integer", nullable: false),
                maximum_risk_score = table.Column<int>(type: "integer", nullable: false),
                manual_review_required = table.Column<bool>(type: "boolean", nullable: false),
                hourly_action_limit = table.Column<int>(type: "integer", nullable: false),
                daily_action_limit = table.Column<int>(type: "integer", nullable: false),
                per_domain_action_limit = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_definitions", x => x.id);
                table.ForeignKey(
                    name: "FK_policy_definitions_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_blocklist_entries_project_id_created_at_id",
            table: "blocklist_entries",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_blocklist_entries_project_id_match_type_value",
            table: "blocklist_entries",
            columns: new[] { "project_id", "match_type", "value" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_opportunity_score_reasons_opportunity_id_kind",
            table: "opportunity_score_reasons",
            columns: new[] { "opportunity_id", "kind" });

        migrationBuilder.CreateIndex(
            name: "IX_policy_definitions_project_id",
            table: "policy_definitions",
            column: "project_id",
            unique: true);

        migrationBuilder.Sql(
            """
            INSERT INTO policy_definitions
                (id, project_id, automation_enabled, minimum_quality_score, maximum_risk_score,
                 manual_review_required, hourly_action_limit, daily_action_limit, per_domain_action_limit,
                 created_at, updated_at)
            SELECT gen_random_uuid(), id, FALSE, 60, 30, TRUE, 10, 50, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM projects
            ON CONFLICT (project_id) DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "blocklist_entries");

        migrationBuilder.DropTable(
            name: "opportunity_score_reasons");

        migrationBuilder.DropTable(
            name: "policy_definitions");

        migrationBuilder.DropColumn(
            name: "analysis_error",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "canonical_url",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "cms",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "content_type",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "eligible_signals",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "existing_target_link",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "final_url",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "http_status",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "requires_javascript",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "robots_directives",
            table: "candidate_pages");

        migrationBuilder.DropColumn(
            name: "title",
            table: "candidate_pages");
    }
}
