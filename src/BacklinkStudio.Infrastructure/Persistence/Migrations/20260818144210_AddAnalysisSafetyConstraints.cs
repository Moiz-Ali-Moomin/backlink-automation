using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAnalysisSafetyConstraints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_policy_daily_limit",
            table: "policy_definitions",
            sql: "daily_action_limit BETWEEN 0 AND 100000");

        migrationBuilder.AddCheckConstraint(
            name: "ck_policy_domain_limit",
            table: "policy_definitions",
            sql: "per_domain_action_limit BETWEEN 0 AND 10000");

        migrationBuilder.AddCheckConstraint(
            name: "ck_policy_hourly_limit",
            table: "policy_definitions",
            sql: "hourly_action_limit BETWEEN 0 AND 10000");

        migrationBuilder.AddCheckConstraint(
            name: "ck_policy_quality_score",
            table: "policy_definitions",
            sql: "minimum_quality_score BETWEEN 0 AND 100");

        migrationBuilder.AddCheckConstraint(
            name: "ck_policy_risk_score",
            table: "policy_definitions",
            sql: "maximum_risk_score BETWEEN 0 AND 100");

        migrationBuilder.AddCheckConstraint(
            name: "ck_opportunity_score_reason_points",
            table: "opportunity_score_reasons",
            sql: "points BETWEEN -100 AND 100");

        migrationBuilder.AddCheckConstraint(
            name: "ck_opportunities_quality_score",
            table: "opportunities",
            sql: "quality_score BETWEEN 0 AND 100");

        migrationBuilder.AddCheckConstraint(
            name: "ck_opportunities_risk_score",
            table: "opportunities",
            sql: "risk_score BETWEEN 0 AND 100");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_policy_daily_limit",
            table: "policy_definitions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_policy_domain_limit",
            table: "policy_definitions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_policy_hourly_limit",
            table: "policy_definitions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_policy_quality_score",
            table: "policy_definitions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_policy_risk_score",
            table: "policy_definitions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_opportunity_score_reason_points",
            table: "opportunity_score_reasons");

        migrationBuilder.DropCheckConstraint(
            name: "ck_opportunities_quality_score",
            table: "opportunities");

        migrationBuilder.DropCheckConstraint(
            name: "ck_opportunities_risk_score",
            table: "opportunities");
    }
}
