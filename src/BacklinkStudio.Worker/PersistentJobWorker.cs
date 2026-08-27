using System.Diagnostics;
using System.Threading.Channels;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Worker;

public sealed partial class PersistentJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    IJobFailureClassifier failureClassifier,
    IRetryDelayPolicy retryDelayPolicy,
    TimeProvider timeProvider,
    ILogger<PersistentJobWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";
    private int _activeJobs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterAsync(stoppingToken);
        var channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(_options.BufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.Concurrency == 1
        });

        LogStarted(logger, _workerId, _options.Concurrency, _options.BufferSize);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = PublishHeartbeatsAsync(heartbeatCancellation.Token);
        var producer = DispatchAsync(channel.Writer, stoppingToken);
        var consumers = Enumerable.Range(0, _options.Concurrency)
            .Select(_ => ConsumeAsync(channel.Reader, stoppingToken))
            .ToArray();

        try
        {
            await producer;
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(consumers);
        }
        finally
        {
            heartbeatCancellation.Cancel();
            await IgnoreCancellationAsync(heartbeat);
            await MarkStoppedAsync();
        }
    }

    private async Task DispatchAsync(ChannelWriter<byte> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (writer.TryWrite(0))
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), timeProvider, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStoppedClaiming(logger, _workerId);
        }
    }

    private async Task ConsumeAsync(ChannelReader<byte> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in reader.ReadAllAsync(cancellationToken))
            {
                PersistentJob? job;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    job = await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync(
                        _workerId,
                        TimeSpan.FromSeconds(_options.ClaimLeaseSeconds),
                        ClaimLimits(),
                        timeProvider.GetUtcNow(),
                        cancellationToken);
                }

                if (job is null)
                {
                    continue;
                }

                BacklinkStudioTelemetry.JobsClaimed.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
                if (job.RecoveryCount > 0)
                {
                    BacklinkStudioTelemetry.JobsRecovered.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
                }
                try
                {
                    await ProcessAsync(job, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogProcessingPersistenceFailed(logger, exception, _workerId, job.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogConsumerStopped(logger, _workerId);
        }
    }

    private async Task ProcessAsync(PersistentJob job, CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var executor = scope.ServiceProvider.GetServices<IJobExecutor>().SingleOrDefault(x => x.JobType == job.Type);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var leaseCancellation = new CancellationTokenSource();
        var leaseState = new LeaseState();
        Task leaseMonitor = Task.CompletedTask;
        var activeJobs = Interlocked.Increment(ref _activeJobs);
        BacklinkStudioTelemetry.WorkerActiveJobs.Add(1);
        BacklinkStudioTelemetry.UpdateWorkerUtilization(activeJobs, _options.Concurrency);

        try
        {
            await queue.MarkRunningAsync(job.Id, _workerId, TimeSpan.FromSeconds(_options.ClaimLeaseSeconds), timeProvider.GetUtcNow(), stoppingToken);
            leaseMonitor = MaintainLeaseAsync(job.Id, executionCancellation, leaseState, leaseCancellation.Token);
            if (executor is null)
            {
                throw new ValidationException($"No executor is registered for job type {job.Type}.");
            }
            using var activity = BacklinkStudioTelemetry.Activities.StartActivity("job.execute");
            activity?.SetTag("job.id", job.Id);
            activity?.SetTag("job.type", job.Type.ToString());
            activity?.SetTag("project.id", job.ProjectId);
            await executor.ExecuteAsync(job, _workerId, executionCancellation.Token);
            await StopLeaseMonitorAsync(leaseCancellation, leaseMonitor);
            var finalLease = await ConfirmLeaseAsync(queue, job.Id);
            if (finalLease == JobLeaseRenewal.Lost)
            {
                LogLeaseLost(logger, _workerId, job.Id);
                return;
            }
            if (finalLease == JobLeaseRenewal.PauseRequested)
            {
                await AcknowledgePauseAsync(queue, job.Id);
                return;
            }

            await queue.MarkSucceededAsync(job.Id, _workerId, timeProvider.GetUtcNow(), CancellationToken.None);
            BacklinkStudioTelemetry.JobsCompleted.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
            LogCompleted(logger, _workerId, job.Id, job.ProjectId, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (leaseState.Outcome == JobLeaseRenewal.PauseRequested)
        {
            await StopLeaseMonitorAsync(leaseCancellation, leaseMonitor);
            await AcknowledgePauseAsync(queue, job.Id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await StopLeaseMonitorAsync(leaseCancellation, leaseMonitor);
            await ReleaseClaimAsync(job.Id);
            LogReleased(logger, _workerId, job.Id);
        }
        catch (OperationCanceledException) when (leaseState.Outcome == JobLeaseRenewal.Lost)
        {
            await StopLeaseMonitorAsync(leaseCancellation, leaseMonitor);
            LogLeaseLost(logger, _workerId, job.Id);
        }
        catch (Exception exception)
        {
            await StopLeaseMonitorAsync(leaseCancellation, leaseMonitor);
            if (leaseState.Outcome == JobLeaseRenewal.Lost)
            {
                LogLeaseLost(logger, _workerId, job.Id);
                return;
            }

            var finalLease = await ConfirmLeaseAsync(queue, job.Id);
            if (finalLease == JobLeaseRenewal.Lost)
            {
                LogLeaseLost(logger, _workerId, job.Id);
                return;
            }
            if (finalLease == JobLeaseRenewal.PauseRequested)
            {
                await AcknowledgePauseAsync(queue, job.Id);
                return;
            }

            var failure = failureClassifier.Classify(exception);
            var delay = retryDelayPolicy.GetDelay(job.Id, job.AttemptCount);
            if (exception is RateLimitExceededException { RetryAfter: { } retryAfter } && retryAfter > delay)
            {
                delay = retryAfter;
            }
            var error = $"{exception.GetType().Name}: {exception.Message}";
            if (error.Length > 4_000)
            {
                error = error[..4_000];
            }

            await queue.MarkFailedAsync(job.Id, _workerId, error, failure, timeProvider.GetUtcNow(), delay, CancellationToken.None);
            BacklinkStudioTelemetry.JobsFailed.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
            if (failure.Retryable && job.AttemptCount < job.MaxAttempts)
            {
                BacklinkStudioTelemetry.JobsRetried.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
            }
            else if (failure.Retryable)
            {
                BacklinkStudioTelemetry.JobsDeadLettered.Add(1, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
            }
            LogFailed(logger, exception, _workerId, job.Id, failure.Retryable, failure.Kind);
        }
        finally
        {
            leaseCancellation.Cancel();
            activeJobs = Interlocked.Decrement(ref _activeJobs);
            BacklinkStudioTelemetry.WorkerActiveJobs.Add(-1);
            BacklinkStudioTelemetry.UpdateWorkerUtilization(activeJobs, _options.Concurrency);
            BacklinkStudioTelemetry.JobDuration.Record(stopwatch.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("job.type", job.Type.ToString()));
        }
    }

    private async Task MaintainLeaseAsync(Guid jobId, CancellationTokenSource executionCancellation, LeaseState state, CancellationToken cancellationToken)
    {
        var leaseDeadline = timeProvider.GetUtcNow().AddSeconds(_options.ClaimLeaseSeconds);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.LeaseRenewalSeconds), timeProvider, cancellationToken);
                JobLeaseRenewal outcome;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var now = timeProvider.GetUtcNow();
                    outcome = await scope.ServiceProvider.GetRequiredService<IJobQueue>().RenewLeaseAsync(
                        jobId,
                        _workerId,
                        TimeSpan.FromSeconds(_options.ClaimLeaseSeconds),
                        now,
                        cancellationToken);
                    leaseDeadline = now.AddSeconds(_options.ClaimLeaseSeconds);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogLeaseRenewalFailed(logger, exception, _workerId, jobId);
                    if (timeProvider.GetUtcNow() < leaseDeadline)
                    {
                        continue;
                    }
                    outcome = JobLeaseRenewal.Lost;
                }
                if (outcome == JobLeaseRenewal.Renewed)
                {
                    continue;
                }

                state.Outcome = outcome;
                if (outcome == JobLeaseRenewal.Lost)
                {
                    BacklinkStudioTelemetry.JobLeasesLost.Add(1);
                }
                executionCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishHeartbeatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IWorkerRegistry>().HeartbeatAsync(_workerId, Volatile.Read(ref _activeJobs), timeProvider.GetUtcNow(), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogHeartbeatFailed(logger, exception, _workerId);
                }
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), timeProvider, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var now = timeProvider.GetUtcNow();
        await scope.ServiceProvider.GetRequiredService<IWorkerRegistry>().RegisterAsync(new WorkerRegistration(
            _workerId,
            Environment.MachineName,
            Environment.ProcessId,
            _options.Concurrency,
            _options.BufferSize,
            now), cancellationToken);
    }

    private async Task MarkStoppedAsync()
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWorkerRegistry>().MarkStoppedAsync(_workerId, timeProvider.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogHeartbeatStopFailed(logger, exception, _workerId);
        }
    }

    private async Task AcknowledgePauseAsync(IJobQueue queue, Guid jobId)
    {
        try
        {
            await queue.AcknowledgePauseAsync(jobId, _workerId, timeProvider.GetUtcNow(), CancellationToken.None);
            BacklinkStudioTelemetry.JobsPaused.Add(1);
            LogPaused(logger, _workerId, jobId);
        }
        catch (Exception exception)
        {
            LogPauseFailed(logger, exception, _workerId, jobId);
        }
    }

    private async Task ReleaseClaimAsync(Guid jobId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IJobQueue>().ReleaseAsync(jobId, _workerId, timeProvider.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(logger, exception, _workerId, jobId);
        }
    }

    private JobClaimLimits ClaimLimits() => new(
        _options.GlobalConcurrency,
        _options.PerProjectConcurrency,
        _options.PerCampaignConcurrency,
        _options.PerDomainConcurrency);

    private Task<JobLeaseRenewal> ConfirmLeaseAsync(IJobQueue queue, Guid jobId) => queue.RenewLeaseAsync(
        jobId,
        _workerId,
        TimeSpan.FromSeconds(_options.ClaimLeaseSeconds),
        timeProvider.GetUtcNow(),
        CancellationToken.None);

    private static async Task StopLeaseMonitorAsync(CancellationTokenSource cancellation, Task monitor)
    {
        cancellation.Cancel();
        await IgnoreCancellationAsync(monitor);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class LeaseState
    {
        private int _outcome = (int)JobLeaseRenewal.Renewed;
        public JobLeaseRenewal Outcome
        {
            get => (JobLeaseRenewal)Volatile.Read(ref _outcome);
            set => Volatile.Write(ref _outcome, (int)value);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Worker {WorkerId} started with concurrency {Concurrency} and buffer {BufferSize}")]
    private static partial void LogStarted(ILogger logger, string workerId, int concurrency, int bufferSize);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Worker {WorkerId} stopped claiming jobs")]
    private static partial void LogStoppedClaiming(ILogger logger, string workerId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Worker {WorkerId} consumer stopped")]
    private static partial void LogConsumerStopped(ILogger logger, string workerId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Worker {WorkerId} completed job {JobId} for project {ProjectId} in {DurationMs} ms")]
    private static partial void LogCompleted(ILogger logger, string workerId, Guid jobId, Guid projectId, long durationMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Worker {WorkerId} released job {JobId} during shutdown")]
    private static partial void LogReleased(ILogger logger, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Worker {WorkerId} failed job {JobId}; retryable={Retryable}; failureKind={FailureKind}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string workerId, Guid jobId, bool retryable, JobFailureKind failureKind);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Worker {WorkerId} could not release job {JobId}; its lease will expire")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "Worker {WorkerId} lost the lease for job {JobId}")]
    private static partial void LogLeaseLost(ILogger logger, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "Worker {WorkerId} could not renew the lease for job {JobId}; renewal will be retried")]
    private static partial void LogLeaseRenewalFailed(ILogger logger, Exception exception, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Worker {WorkerId} paused job {JobId}")]
    private static partial void LogPaused(ILogger logger, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Worker {WorkerId} could not acknowledge the pause for job {JobId}; its lease will expire")]
    private static partial void LogPauseFailed(ILogger logger, Exception exception, string workerId, Guid jobId);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Worker {WorkerId} could not persist its stopped heartbeat")]
    private static partial void LogHeartbeatStopFailed(ILogger logger, Exception exception, string workerId);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Worker {WorkerId} could not publish its heartbeat; publication will be retried")]
    private static partial void LogHeartbeatFailed(ILogger logger, Exception exception, string workerId);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Error, Message = "Worker {WorkerId} could not persist the terminal state for job {JobId}; its lease will remain recoverable")]
    private static partial void LogProcessingPersistenceFailed(ILogger logger, Exception exception, string workerId, Guid jobId);
}
