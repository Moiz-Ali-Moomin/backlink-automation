using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSubmissionContentAndValidation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "additional_required_fields",
            table: "submission_sources",
            type: "text[]",
            nullable: false,
                defaultValue: Array.Empty<string>());

        migrationBuilder.AddColumn<string>(
            name: "comment_author_field",
            table: "submission_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "comment_content_field",
            table: "submission_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "comment_email_field",
            table: "submission_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "comment_post_id_field",
            table: "submission_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "comment_website_field",
            table: "submission_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "comments_enabled",
            table: "submission_sources",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "final_url",
            table: "submission_sources",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "moderation_signal",
            table: "submission_sources",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "requires_cookies",
            table: "submission_sources",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "requires_nonce",
            table: "submission_sources",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "submission_identity_pools",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                selection_strategy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                email_strategy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                email_base_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                catch_all_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_identity_pools", x => x.id);
                table.ForeignKey(
                    name: "FK_submission_identity_pools_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_template_pools",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                template_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                selection_strategy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                placement_method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_template_pools", x => x.id);
                table.ForeignKey(
                    name: "FK_submission_template_pools_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "wordpress_site_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owned_network_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                api_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                credential_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                submission_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wordpress_site_profiles", x => x.id);
                table.ForeignKey(
                    name: "FK_wordpress_site_profiles_owned_network_profiles_owned_networ~",
                    column: x => x.owned_network_profile_id,
                    principalTable: "owned_network_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_identities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                website = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                organization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                weight = table.Column<int>(type: "integer", nullable: false),
                usage_count = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_identities", x => x.id);
                table.CheckConstraint("ck_submission_identities_usage_count", "usage_count >= 0");
                table.CheckConstraint("ck_submission_identities_weight", "weight BETWEEN 1 AND 10000");
                table.ForeignKey(
                    name: "FK_submission_identities_submission_identity_pools_pool_id",
                    column: x => x.pool_id,
                    principalTable: "submission_identity_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "submission_templates",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                prefix_variants = table.Column<string[]>(type: "text[]", nullable: false),
                suffix_variants = table.Column<string[]>(type: "text[]", nullable: false),
                anchor_variants = table.Column<string[]>(type: "text[]", nullable: false),
                target_url_variants = table.Column<string[]>(type: "text[]", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                weight = table.Column<int>(type: "integer", nullable: false),
                usage_count = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_templates", x => x.id);
                table.CheckConstraint("ck_submission_templates_usage_count", "usage_count >= 0");
                table.CheckConstraint("ck_submission_templates_variant_limits", "cardinality(prefix_variants) <= 100 AND cardinality(suffix_variants) <= 100 AND cardinality(anchor_variants) <= 100 AND cardinality(target_url_variants) <= 100");
                table.CheckConstraint("ck_submission_templates_weight", "weight BETWEEN 1 AND 10000");
                table.ForeignKey(
                    name: "FK_submission_templates_submission_template_pools_pool_id",
                    column: x => x.pool_id,
                    principalTable: "submission_template_pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_submission_identities_pool_id_email",
            table: "submission_identities",
            columns: new[] { "pool_id", "email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_identities_pool_id_enabled_created_at_id",
            table: "submission_identities",
            columns: new[] { "pool_id", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_identity_pools_project_id_enabled_created_at_id",
            table: "submission_identity_pools",
            columns: new[] { "project_id", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_identity_pools_project_id_name",
            table: "submission_identity_pools",
            columns: new[] { "project_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_template_pools_project_id_enabled_created_at_id",
            table: "submission_template_pools",
            columns: new[] { "project_id", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_template_pools_project_id_name",
            table: "submission_template_pools",
            columns: new[] { "project_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_submission_templates_pool_id_enabled_created_at_id",
            table: "submission_templates",
            columns: new[] { "pool_id", "enabled", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_submission_templates_pool_id_name",
            table: "submission_templates",
            columns: new[] { "pool_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_wordpress_site_profiles_domain_enabled",
            table: "wordpress_site_profiles",
            columns: new[] { "domain", "enabled" });

        migrationBuilder.CreateIndex(
            name: "IX_wordpress_site_profiles_owned_network_profile_id_domain",
            table: "wordpress_site_profiles",
            columns: new[] { "owned_network_profile_id", "domain" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "submission_identities");

        migrationBuilder.DropTable(
            name: "submission_templates");

        migrationBuilder.DropTable(
            name: "wordpress_site_profiles");

        migrationBuilder.DropTable(
            name: "submission_identity_pools");

        migrationBuilder.DropTable(
            name: "submission_template_pools");

        migrationBuilder.DropColumn(
            name: "additional_required_fields",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comment_author_field",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comment_content_field",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comment_email_field",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comment_post_id_field",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comment_website_field",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "comments_enabled",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "final_url",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "moderation_signal",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "requires_cookies",
            table: "submission_sources");

        migrationBuilder.DropColumn(
            name: "requires_nonce",
            table: "submission_sources");
    }
}
