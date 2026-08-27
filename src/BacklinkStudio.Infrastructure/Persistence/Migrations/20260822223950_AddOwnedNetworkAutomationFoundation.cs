using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOwnedNetworkAutomationFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "owned_network_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ownership_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                automation_permitted = table.Column<bool>(type: "boolean", nullable: false),
                optional_network_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                default_identity_pool_id = table.Column<Guid>(type: "uuid", nullable: true),
                default_template_pool_id = table.Column<Guid>(type: "uuid", nullable: true),
                max_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_concurrency = table.Column<int>(type: "integer", nullable: false),
                per_domain_delay_milliseconds = table.Column<int>(type: "integer", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_owned_network_profiles", x => x.id);
                table.CheckConstraint("ck_owned_network_profiles_automation_ownership", "NOT automation_permitted OR ownership_status <> 'Unverified'");
                table.CheckConstraint("ck_owned_network_profiles_concurrency", "max_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND max_concurrency");
                table.CheckConstraint("ck_owned_network_profiles_domain_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
                table.ForeignKey(
                    name: "FK_owned_network_profiles_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "owned_network_domains",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owned_network_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                match_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_owned_network_domains", x => x.id);
                table.ForeignKey(
                    name: "FK_owned_network_domains_owned_network_profiles_owned_network_~",
                    column: x => x.owned_network_profile_id,
                    principalTable: "owned_network_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_source_imports",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                owned_network_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: true),
                format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                byte_length = table.Column<long>(type: "bigint", nullable: false),
                total_lines = table.Column<int>(type: "integer", nullable: false),
                accepted = table.Column<int>(type: "integer", nullable: false),
                duplicates = table.Column<int>(type: "integer", nullable: false),
                invalid = table.Column<int>(type: "integer", nullable: false),
                errors = table.Column<int>(type: "integer", nullable: false),
                safe_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_source_imports", x => x.id);
                table.CheckConstraint("ck_submission_source_imports_byte_length", "byte_length >= 0");
                table.CheckConstraint("ck_submission_source_imports_counters", "total_lines >= 0 AND accepted >= 0 AND duplicates >= 0 AND invalid >= 0 AND errors >= 0");
                table.ForeignKey(
                    name: "FK_submission_source_imports_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_submission_source_imports_owned_network_profiles_owned_netw~",
                    column: x => x.owned_network_profile_id,
                    principalTable: "owned_network_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_submission_source_imports_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_source_import_chunks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<int>(type: "integer", nullable: false),
                content = table.Column<byte[]>(type: "bytea", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_source_import_chunks", x => x.id);
                table.CheckConstraint("ck_submission_source_import_chunks_sequence", "sequence >= 0");
                table.CheckConstraint("ck_submission_source_import_chunks_size", "octet_length(content) BETWEEN 1 AND 65536");
                table.ForeignKey(
                    name: "FK_submission_source_import_chunks_submission_source_imports_i~",
                    column: x => x.import_id,
                    principalTable: "submission_source_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_sources",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                owned_network_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_import_id = table.Column<Guid>(type: "uuid", nullable: true),
                original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                cms_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                opportunity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                adapter_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ownership_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                automation_permitted = table.Column<bool>(type: "boolean", nullable: false),
                technical_compatibility = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                validation_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                validation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                detection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                requires_browser = table.Column<bool>(type: "boolean", nullable: false),
                requires_authentication = table.Column<bool>(type: "boolean", nullable: false),
                requires_manual_action = table.Column<bool>(type: "boolean", nullable: false),
                supports_wordpress_comment = table.Column<bool>(type: "boolean", nullable: false),
                supports_owned_wordpress_api = table.Column<bool>(type: "boolean", nullable: false),
                supports_owned_property_placement = table.Column<bool>(type: "boolean", nullable: false),
                post_id = table.Column<long>(type: "bigint", nullable: true),
                comment_endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                detected_form_action = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                page_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                canonical_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                last_http_status = table.Column<int>(type: "integer", nullable: true),
                last_content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                last_content_length = table.Column<long>(type: "bigint", nullable: true),
                redirect_chain = table.Column<string[]>(type: "text[]", nullable: false),
                tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                last_validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_submission_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_successful_submission_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                success_count = table.Column<long>(type: "bigint", nullable: false),
                failure_count = table.Column<long>(type: "bigint", nullable: false),
                pending_moderation_count = table.Column<long>(type: "bigint", nullable: false),
                verified_count = table.Column<long>(type: "bigint", nullable: false),
                lost_count = table.Column<long>(type: "bigint", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_sources", x => x.id);
                table.CheckConstraint("ck_submission_sources_automation_ownership", "NOT automation_permitted OR ownership_status <> 'Unverified'");
                table.CheckConstraint("ck_submission_sources_content_length", "last_content_length IS NULL OR last_content_length >= 0");
                table.CheckConstraint("ck_submission_sources_counters", "success_count >= 0 AND failure_count >= 0 AND pending_moderation_count >= 0 AND verified_count >= 0 AND lost_count >= 0");
                table.ForeignKey(
                    name: "FK_submission_sources_owned_network_profiles_owned_network_pro~",
                    column: x => x.owned_network_profile_id,
                    principalTable: "owned_network_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_submission_sources_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_submission_sources_submission_source_imports_source_import_~",
                    column: x => x.source_import_id,
                    principalTable: "submission_source_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_domains_domain_enabled",
            table: "owned_network_domains",
            columns: new[] { "domain", "enabled" });

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_domains_owned_network_profile_id_domain_match~",
            table: "owned_network_domains",
            columns: new[] { "owned_network_profile_id", "domain", "match_type" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_profiles_ownership_status_automation_permitte~",
            table: "owned_network_profiles",
            columns: new[] { "ownership_status", "automation_permitted", "enabled" });

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_profiles_project_id_enabled_created_at_id",
            table: "owned_network_profiles",
            columns: new[] { "project_id", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_owned_network_profiles_project_id_name",
            table: "owned_network_profiles",
            columns: new[] { "project_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_import_chunks_import_id_sequence",
            table: "submission_source_import_chunks",
            columns: new[] { "import_id", "sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_imports_job_id",
            table: "submission_source_imports",
            column: "job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_imports_owned_network_profile_id",
            table: "submission_source_imports",
            column: "owned_network_profile_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_imports_project_id_created_at_id",
            table: "submission_source_imports",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_imports_project_id_idempotency_key",
            table: "submission_source_imports",
            columns: new[] { "project_id", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_source_imports_status_created_at",
            table: "submission_source_imports",
            columns: new[] { "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_adapter_name_created_at_id",
            table: "submission_sources",
            columns: new[] { "adapter_name", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_cms_type_created_at_id",
            table: "submission_sources",
            columns: new[] { "cms_type", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_domain_created_at_id",
            table: "submission_sources",
            columns: new[] { "domain", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_host_created_at_id",
            table: "submission_sources",
            columns: new[] { "host", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_last_validated_at",
            table: "submission_sources",
            column: "last_validated_at");

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_owned_network_profile_id_created_at_id",
            table: "submission_sources",
            columns: new[] { "owned_network_profile_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_ownership_status_automation_permitted_en~",
            table: "submission_sources",
            columns: new[] { "ownership_status", "automation_permitted", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_platform_created_at_id",
            table: "submission_sources",
            columns: new[] { "platform", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_project_id_created_at_id",
            table: "submission_sources",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_project_id_normalized_url",
            table: "submission_sources",
            columns: new[] { "project_id", "normalized_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_source_import_id",
            table: "submission_sources",
            column: "source_import_id");

        migrationBuilder.CreateIndex(
            name: "IX_submission_sources_technical_compatibility_validation_statu~",
            table: "submission_sources",
            columns: new[] { "technical_compatibility", "validation_status", "enabled", "created_at", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "owned_network_domains");

        migrationBuilder.DropTable(
            name: "submission_source_import_chunks");

        migrationBuilder.DropTable(
            name: "submission_sources");

        migrationBuilder.DropTable(
            name: "submission_source_imports");

        migrationBuilder.DropTable(
            name: "owned_network_profiles");
    }
}
