using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCampaignStateFiltersAndBacklinkProvenance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_submission_source_id",
            table: "submission_jobs");

        migrationBuilder.AddColumn<string>(
            name: "previous_submission_status",
            table: "owned_network_campaign_configurations",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "previous_verification_status",
            table: "owned_network_campaign_configurations",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "submission_attempt_id",
            table: "backlinks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "submission_source_id",
            table: "backlinks",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE backlinks AS backlink
            SET submission_source_id = submission.submission_source_id
            FROM submission_jobs AS submission
            WHERE backlink.submission_job_id = submission.id
              AND backlink.submission_source_id IS NULL
              AND submission.submission_source_id IS NOT NULL;

            UPDATE backlinks AS backlink
            SET submission_attempt_id = (
                SELECT attempt.id
                FROM submission_attempts AS attempt
                WHERE attempt.submission_job_id = backlink.submission_job_id
                ORDER BY attempt.attempt_number DESC
                LIMIT 1
            )
            WHERE backlink.submission_attempt_id IS NULL
              AND EXISTS (
                  SELECT 1
                  FROM submission_attempts AS attempt
                  WHERE attempt.submission_job_id = backlink.submission_job_id
              );
            """);

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_submission_source_id_status",
            table: "submission_jobs",
            columns: new[] { "submission_source_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_submission_attempt_id",
            table: "backlinks",
            column: "submission_attempt_id",
            unique: true,
            filter: "submission_attempt_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_backlinks_submission_source_id_status",
            table: "backlinks",
            columns: new[] { "submission_source_id", "status" });

        migrationBuilder.AddForeignKey(
            name: "FK_backlinks_submission_attempts_submission_attempt_id",
            table: "backlinks",
            column: "submission_attempt_id",
            principalTable: "submission_attempts",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_backlinks_submission_sources_submission_source_id",
            table: "backlinks",
            column: "submission_source_id",
            principalTable: "submission_sources",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_backlinks_submission_attempts_submission_attempt_id",
            table: "backlinks");

        migrationBuilder.DropForeignKey(
            name: "FK_backlinks_submission_sources_submission_source_id",
            table: "backlinks");

        migrationBuilder.DropIndex(
            name: "IX_submission_jobs_submission_source_id_status",
            table: "submission_jobs");

        migrationBuilder.DropIndex(
            name: "IX_backlinks_submission_attempt_id",
            table: "backlinks");

        migrationBuilder.DropIndex(
            name: "IX_backlinks_submission_source_id_status",
            table: "backlinks");

        migrationBuilder.DropColumn(
            name: "previous_submission_status",
            table: "owned_network_campaign_configurations");

        migrationBuilder.DropColumn(
            name: "previous_verification_status",
            table: "owned_network_campaign_configurations");

        migrationBuilder.DropColumn(
            name: "submission_attempt_id",
            table: "backlinks");

        migrationBuilder.DropColumn(
            name: "submission_source_id",
            table: "backlinks");

        migrationBuilder.CreateIndex(
            name: "IX_submission_jobs_submission_source_id",
            table: "submission_jobs",
            column: "submission_source_id");
    }
}
