namespace BacklinkStudio.Mcp;

public sealed class McpRateLimitOptions
{
    public const string SectionName = "McpRateLimit";
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;

    public void Validate()
    {
        if (PermitLimit is < 1 or > 1_000_000) throw new InvalidOperationException("MCP rate-limit permit count is invalid.");
        if (WindowSeconds is < 1 or > 3_600) throw new InvalidOperationException("MCP rate-limit window is invalid.");
    }
}
