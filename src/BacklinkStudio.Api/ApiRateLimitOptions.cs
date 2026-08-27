namespace BacklinkStudio.Api;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "ApiRateLimit";
    public int PermitLimit { get; init; } = 120;
    public int WindowSeconds { get; init; } = 60;

    public void Validate()
    {
        if (PermitLimit is < 1 or > 1_000_000) throw new InvalidOperationException("API rate-limit permit count is invalid.");
        if (WindowSeconds is < 1 or > 3_600) throw new InvalidOperationException("API rate-limit window is invalid.");
    }
}
