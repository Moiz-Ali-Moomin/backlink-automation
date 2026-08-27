using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record JobClaimLimits(
    int GlobalConcurrency,
    int PerProjectConcurrency,
    int PerCampaignConcurrency,
    int PerDomainConcurrency);

public enum JobLeaseRenewal
{
    Renewed,
    PauseRequested,
    Lost
}

public sealed record WorkerRegistration(
    string WorkerId,
    string MachineName,
    int ProcessId,
    int Concurrency,
    int BufferSize,
    DateTimeOffset StartedAt);

public sealed record JobFailureDecision(bool Retryable, JobFailureKind Kind);

public interface IJobFailureClassifier
{
    JobFailureDecision Classify(Exception exception);
}

public interface IRetryDelayPolicy
{
    TimeSpan GetDelay(Guid jobId, int attemptCount);
}

public sealed class DefaultJobFailureClassifier : IJobFailureClassifier
{
    public JobFailureDecision Classify(Exception exception) => exception switch
    {
        RateLimitExceededException => new(true, JobFailureKind.RateLimited),
        TimeoutException or TaskCanceledException => new(true, JobFailureKind.Timeout),
        HttpRequestException http when http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => new(true, JobFailureKind.RateLimited),
        HttpRequestException http when http.StatusCode is not null && (int)http.StatusCode.Value is >= 400 and < 500 => new(false, JobFailureKind.InvalidInput),
        HttpRequestException => new(true, JobFailureKind.DependencyUnavailable),
        UnauthorizedAccessException => new(false, JobFailureKind.AuthorizationDenied),
        PolicyRejectedException => new(false, JobFailureKind.PolicyRejected),
        NotSupportedException => new(false, JobFailureKind.Unsupported),
        ValidationException or DomainRuleException or FormatException or JsonException or UriFormatException => new(false, JobFailureKind.InvalidInput),
        _ => new(true, JobFailureKind.Unknown)
    };
}

public sealed class ExponentialBackoffRetryPolicy(TimeSpan baseDelay, TimeSpan maximumDelay) : IRetryDelayPolicy
{
    public TimeSpan GetDelay(Guid jobId, int attemptCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);

        var exponent = Math.Min(attemptCount - 1, 20);
        var exponentialMilliseconds = baseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedMilliseconds = Math.Min(exponentialMilliseconds, maximumDelay.TotalMilliseconds);
        Span<byte> input = stackalloc byte[20];
        jobId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], attemptCount);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var jitterFraction = BitConverter.ToUInt32(hash) / (double)uint.MaxValue;
        var jitteredMilliseconds = Math.Min(maximumDelay.TotalMilliseconds, cappedMilliseconds * (0.8 + (0.4 * jitterFraction)));
        return TimeSpan.FromMilliseconds(Math.Max(1, jitteredMilliseconds));
    }
}

public sealed class RateLimitExceededException(string message, TimeSpan? retryAfter = null) : BacklinkStudioException(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromDays(1)
        ? value
        : null;
}
