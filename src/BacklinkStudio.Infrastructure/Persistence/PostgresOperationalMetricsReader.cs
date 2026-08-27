using System.Data;
using BacklinkStudio.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class PostgresOperationalMetricsReader(BacklinkStudioDbContext dbContext) : IOperationalMetricsReader
{
    public async Task<OperationalMetricsSnapshot> ReadAsync(
        DateTimeOffset now,
        DateTimeOffset activeWorkerCutoff,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*) FILTER (WHERE status IN ('Queued', 'RetryScheduled'))::bigint AS queue_depth,
                COUNT(*) FILTER (WHERE status IN ('Claimed', 'Running') AND claim_expires_at > @now)::bigint AS running_jobs,
                COUNT(*) FILTER (WHERE status = 'Failed')::bigint AS failed_jobs,
                COUNT(*) FILTER (WHERE status = 'DeadLetter')::bigint AS dead_letter_jobs,
                (SELECT COUNT(*)::bigint FROM worker_heartbeats WHERE stopped_at IS NULL AND last_heartbeat_at >= @active_worker_cutoff) AS online_workers,
                (SELECT COUNT(*)::bigint FROM schedules WHERE status = 'Active' AND next_run_at <= @now AND available_at <= @now) AS due_schedules
            FROM jobs;
            """;
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
        command.Parameters.Add(new NpgsqlParameter("active_worker_cutoff", NpgsqlDbType.TimestampTz) { Value = activeWorkerCutoff });

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("PostgreSQL did not return an operational metrics snapshot.");
        }

        return new OperationalMetricsSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }
}
