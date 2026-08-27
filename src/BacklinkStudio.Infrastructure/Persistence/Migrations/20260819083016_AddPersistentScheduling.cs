using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BacklinkStudio.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPersistentScheduling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "schedules",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                action_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                action_payload = table.Column<string>(type: "jsonb", nullable: false),
                recurrence_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                one_time_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                interval_minutes = table.Column<int>(type: "integer", nullable: true),
                time_of_day_utc = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                day_of_week = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                day_of_month = table.Column<int>(type: "integer", nullable: true),
                cron_expression = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_job_count = table.Column<int>(type: "integer", nullable: false),
                consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                recovery_count = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                claimed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                claim_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedules", x => x.id);
                table.CheckConstraint("ck_schedule_day_of_month", "day_of_month IS NULL OR day_of_month BETWEEN 1 AND 31");
                table.CheckConstraint("ck_schedule_failures", "consecutive_failures >= 0");
                table.CheckConstraint("ck_schedule_interval", "interval_minutes IS NULL OR interval_minutes BETWEEN 1 AND 525600");
                table.CheckConstraint("ck_schedule_last_job_count", "last_job_count >= 0");
                table.CheckConstraint("ck_schedule_recoveries", "recovery_count >= 0");
                table.CheckConstraint("ck_schedule_timing", "(recurrence_type = 'OneTime' AND one_time_at IS NOT NULL AND interval_minutes IS NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Interval' AND one_time_at IS NULL AND interval_minutes IS NOT NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Daily' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Weekly' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NOT NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Monthly' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NULL AND day_of_month IS NOT NULL AND cron_expression IS NULL) OR (recurrence_type = 'Cron' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_schedules_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_schedules_claim_expires_at",
            table: "schedules",
            column: "claim_expires_at");

        migrationBuilder.CreateIndex(
            name: "IX_schedules_project_id_created_at_id",
            table: "schedules",
            columns: new[] { "project_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedules_status_next_run_at_available_at",
            table: "schedules",
            columns: new[] { "status", "next_run_at", "available_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "schedules");
    }
}
