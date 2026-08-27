using BacklinkStudio.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed partial class SystemHealthService(
    BacklinkStudioDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<SystemHealthService> logger) : ISystemHealthService
{
    public async Task<SystemHealthDto> CheckAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        bool reachable;
        try
        {
            reachable = await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDatabaseConnectivityCheckFailed(logger, exception);
            return new SystemHealthDto("unhealthy", false, false, now);
        }

        if (!reachable)
        {
            return new SystemHealthDto("unhealthy", false, false, now);
        }

        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            var schemaCurrent = !pendingMigrations.Any();
            return new SystemHealthDto(schemaCurrent ? "healthy" : "degraded", true, schemaCurrent, now);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSchemaReadinessCheckFailed(logger, exception);
            return new SystemHealthDto("degraded", true, false, now);
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Database connectivity check failed")]
    private static partial void LogDatabaseConnectivityCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Database schema readiness check failed")]
    private static partial void LogSchemaReadinessCheckFailed(ILogger logger, Exception exception);
}
