using BacklinkStudio.Application;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class PostgresWorkerRegistry(BacklinkStudioDbContext dbContext) : IWorkerRegistry
{
    public Task RegisterAsync(WorkerRegistration registration, CancellationToken cancellationToken) => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO worker_heartbeats
            (worker_id, machine_name, process_id, concurrency, buffer_size, active_jobs, started_at, last_heartbeat_at, stopped_at)
        VALUES
            ({registration.WorkerId}, {registration.MachineName}, {registration.ProcessId}, {registration.Concurrency}, {registration.BufferSize}, 0, {registration.StartedAt}, {registration.StartedAt}, NULL)
        ON CONFLICT (worker_id) DO UPDATE SET
            machine_name = EXCLUDED.machine_name,
            process_id = EXCLUDED.process_id,
            concurrency = EXCLUDED.concurrency,
            buffer_size = EXCLUDED.buffer_size,
            active_jobs = 0,
            started_at = EXCLUDED.started_at,
            last_heartbeat_at = EXCLUDED.last_heartbeat_at,
            stopped_at = NULL
        """, cancellationToken);

    public Task HeartbeatAsync(string workerId, int activeJobs, DateTimeOffset now, CancellationToken cancellationToken) => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE worker_heartbeats
        SET active_jobs = {activeJobs}, last_heartbeat_at = {now}
        WHERE worker_id = {workerId} AND stopped_at IS NULL
        """, cancellationToken);

    public Task MarkStoppedAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken) => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE worker_heartbeats
        SET active_jobs = 0, last_heartbeat_at = {now}, stopped_at = {now}
        WHERE worker_id = {workerId}
        """, cancellationToken);
}
