using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class OptimizeSubmissionSourceCampaignSelection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_submission_sources_campaign_selection",
            table: "submission_sources",
            columns: new[] { "project_id", "owned_network_profile_id", "technical_compatibility", "validation_status", "created_at", "id" },
            filter: "automation_permitted AND enabled");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_submission_sources_campaign_selection",
            table: "submission_sources");
    }
}
