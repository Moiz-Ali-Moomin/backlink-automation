using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class HardenProductionOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_jobs_claim_expires_at",
            table: "jobs");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_active_project",
            table: "jobs",
            columns: new[] { "project_id", "claim_expires_at" },
            filter: "status IN ('Claimed', 'Running')");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_claim_ready",
            table: "jobs",
            columns: new[] { "priority", "available_at", "created_at" },
            descending: new[] { true, false, false },
            filter: "status IN ('Queued', 'RetryScheduled') AND pause_requested_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_expired_claims",
            table: "jobs",
            column: "claim_expires_at",
            filter: "status IN ('Claimed', 'Running')");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_submission_campaign_completed",
            table: "jobs",
            columns: new[] { "campaign_id", "completed_at" },
            filter: "type = 'Submission' AND status = 'Succeeded'");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_submission_project_campaign_completed",
            table: "jobs",
            columns: new[] { "project_id", "campaign_id", "completed_at" },
            filter: "type = 'Submission' AND status = 'Succeeded'");

        migrationBuilder.CreateIndex(
            name: "ix_jobs_submission_project_domain_completed",
            table: "jobs",
            columns: new[] { "project_id", "domain", "completed_at" },
            filter: "type = 'Submission' AND status = 'Succeeded'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_jobs_active_project",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "ix_jobs_claim_ready",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "ix_jobs_expired_claims",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "ix_jobs_submission_campaign_completed",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "ix_jobs_submission_project_campaign_completed",
            table: "jobs");

        migrationBuilder.DropIndex(
            name: "ix_jobs_submission_project_domain_completed",
            table: "jobs");

        migrationBuilder.CreateIndex(
            name: "IX_jobs_claim_expires_at",
            table: "jobs",
            column: "claim_expires_at");
    }
}
