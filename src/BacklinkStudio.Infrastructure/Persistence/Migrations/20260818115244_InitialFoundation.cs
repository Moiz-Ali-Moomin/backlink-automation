using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: true),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                job_id = table.Column<Guid>(type: "uuid", nullable: true),
                request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                input_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                source_address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", x => x.id);
                table.CheckConstraint("ck_audit_input_summary_length", "length(input_summary) <= 2000");
            });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                scope = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                response_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_records", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                primary_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_projects", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "candidate_sites",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_candidate_sites", x => x.id);
                table.ForeignKey(
                    name: "FK_candidate_sites_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                claim_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                max_attempts = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_jobs", x => x.id);
                table.ForeignKey(
                    name: "FK_jobs_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "project_targets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                keywords = table.Column<string[]>(type: "text[]", nullable: false),
                preferred_anchor = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                priority = table.Column<int>(type: "integer", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_targets", x => x.id);
                table.ForeignKey(
                    name: "FK_project_targets_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "agent_credentials",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                lookup_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                key_salt = table.Column<byte[]>(type: "bytea", nullable: false),
                scopes = table.Column<string[]>(type: "text[]", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_credentials", x => x.id);
                table.ForeignKey(
                    name: "FK_agent_credentials_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "candidate_pages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                candidate_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                analysis_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                last_analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_candidate_pages", x => x.id);
                table.ForeignKey(
                    name: "FK_candidate_pages_candidate_sites_candidate_site_id",
                    column: x => x.candidate_site_id,
                    principalTable: "candidate_sites",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_candidate_pages_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "opportunities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                candidate_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                quality_score = table.Column<int>(type: "integer", nullable: false),
                risk_score = table.Column<int>(type: "integer", nullable: false),
                automation_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                analysis_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opportunities", x => x.id);
                table.ForeignKey(
                    name: "FK_opportunities_candidate_pages_candidate_page_id",
                    column: x => x.candidate_page_id,
                    principalTable: "candidate_pages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_opportunities_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_agent_credentials_lookup_hash",
            table: "agent_credentials",
            column: "lookup_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_agent_credentials_user_id",
            table: "agent_credentials",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_job_id",
            table: "audit_events",
            column: "job_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_project_id_timestamp",
            table: "audit_events",
            columns: new[] { "project_id", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_candidate_pages_candidate_site_id",
            table: "candidate_pages",
            column: "candidate_site_id");

        migrationBuilder.CreateIndex(
            name: "IX_candidate_pages_project_id_analysis_status",
            table: "candidate_pages",
            columns: new[] { "project_id", "analysis_status" });

        migrationBuilder.CreateIndex(
            name: "IX_candidate_pages_project_id_created_at_id",
            table: "candidate_pages",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_candidate_pages_project_id_normalized_url",
            table: "candidate_pages",
            columns: new[] { "project_id", "normalized_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_candidate_sites_project_id_domain",
            table: "candidate_sites",
            columns: new[] { "project_id", "domain" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_expires_at",
            table: "idempotency_records",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_scope_key",
            table: "idempotency_records",
            columns: new[] { "scope", "key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_jobs_claim_expires_at",
            table: "jobs",
            column: "claim_expires_at");

        migrationBuilder.CreateIndex(
            name: "IX_jobs_project_id_created_at_id",
            table: "jobs",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_jobs_project_id_type_idempotency_key",
            table: "jobs",
            columns: new[] { "project_id", "type", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_jobs_status_available_at_priority_created_at",
            table: "jobs",
            columns: new[] { "status", "available_at", "priority", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_candidate_page_id",
            table: "opportunities",
            column: "candidate_page_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_domain",
            table: "opportunities",
            column: "domain");

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_project_id_detected_at_id",
            table: "opportunities",
            columns: new[] { "project_id", "detected_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_project_id_quality_score_risk_score",
            table: "opportunities",
            columns: new[] { "project_id", "quality_score", "risk_score" });

        migrationBuilder.CreateIndex(
            name: "IX_project_targets_project_id_created_at_id",
            table: "project_targets",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_project_targets_project_id_normalized_url",
            table: "project_targets",
            columns: new[] { "project_id", "normalized_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_projects_primary_domain",
            table: "projects",
            column: "primary_domain");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agent_credentials");

        migrationBuilder.DropTable(
            name: "audit_events");

        migrationBuilder.DropTable(
            name: "idempotency_records");

        migrationBuilder.DropTable(
            name: "jobs");

        migrationBuilder.DropTable(
            name: "opportunities");

        migrationBuilder.DropTable(
            name: "project_targets");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "candidate_pages");

        migrationBuilder.DropTable(
            name: "candidate_sites");

        migrationBuilder.DropTable(
            name: "projects");
    }
}
