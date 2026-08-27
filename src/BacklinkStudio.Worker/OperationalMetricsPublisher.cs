using BacklinkStudio.Application;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Worker;

public sealed partial class OperationalMetricsPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<OperationalMetricsOptions> options,
    TimeProvider timeProvider,
    ILogger<OperationalMetricsPublisher> logger) : BackgroundService
{
    private readonly OperationalMetricsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = timeProvider.GetUtcNow();
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var snapshot = await scope.ServiceProvider.GetRequiredService<IOperationalMetricsReader>().ReadAsync(
                        now,
                        now.AddSeconds(-_options.WorkerStaleSeconds),
                        stoppingToken);
                    BacklinkStudioTelemetry.UpdateOperationalMetrics(snapshot);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCollectionFailed(logger, exception);
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Operational metrics collection failed; the previous gauge values remain active")]
    private static partial void LogCollectionFailed(ILogger logger, Exception exception);
}
