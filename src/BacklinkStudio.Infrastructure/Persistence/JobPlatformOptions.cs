namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class JobPlatformOptions
{
    public const string SectionName = "JobPlatform";
    public int PerDomainRequestIntervalMilliseconds { get; init; } = 1_000;
    public int MaximumRateLimitWaitSeconds { get; init; } = 300;
}
