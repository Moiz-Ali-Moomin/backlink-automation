using BacklinkStudio.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BacklinkStudio.Mcp;

public sealed class McpDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(cancellationToken);
        if (!result.DatabaseReachable) return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        return result.SchemaCurrent ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("PostgreSQL has pending migrations.");
    }
}
