using System.Data;
using BacklinkStudio.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class PostgresDomainRateLimiter(
    BacklinkStudioDbContext dbContext,
    IOptions<JobPlatformOptions> options,
    TimeProvider timeProvider) : IDomainRateLimiter
{
    private readonly JobPlatformOptions _options = options.Value;

    public async Task WaitAsync(Guid projectId, Guid? campaignId, string domain, CancellationToken cancellationToken, int? minimumDelayMilliseconds = null)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253)
        {
            throw new ValidationException("Rate-limit domain is invalid.");
        }

        var normalizedDomain = domain.Trim().ToLowerInvariant();
        var scopeKey = $"{projectId:N}:{campaignId?.ToString("N") ?? "project"}";
        var now = timeProvider.GetUtcNow();
        if (minimumDelayMilliseconds is < 0 or > 86_400_000)
            throw new ValidationException("Per-domain request delay must be between 0 and 86400000 milliseconds.");
        var interval = TimeSpan.FromMilliseconds(minimumDelayMilliseconds ?? _options.PerDomainRequestIntervalMilliseconds);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO domain_rate_limits
                (scope_key, domain, project_id, campaign_id, next_allowed_at, updated_at)
            VALUES
                (@scope_key, @domain, @project_id, @campaign_id, @now + @interval, @now)
            ON CONFLICT (scope_key, domain) DO UPDATE SET
                next_allowed_at = GREATEST(domain_rate_limits.next_allowed_at, @now) + @interval,
                updated_at = @now
            RETURNING next_allowed_at - @interval;
            """;
        command.Parameters.AddWithValue("scope_key", scopeKey);
        command.Parameters.AddWithValue("domain", normalizedDomain);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add(new NpgsqlParameter("campaign_id", NpgsqlDbType.Uuid) { Value = campaignId is null ? DBNull.Value : campaignId.Value });
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("interval", interval);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var permittedAt = result switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The domain rate-limit reservation did not return a timestamp.")
        };
        var delay = permittedAt - now;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        if (delay > TimeSpan.FromSeconds(_options.MaximumRateLimitWaitSeconds))
        {
            throw new RateLimitExceededException("The per-domain request queue exceeded its configured wait limit.");
        }

        BacklinkStudioTelemetry.RateLimitDelays.Add(1, new KeyValuePair<string, object?>("server.address", normalizedDomain));
        await Task.Delay(delay, timeProvider, cancellationToken);
    }
}
