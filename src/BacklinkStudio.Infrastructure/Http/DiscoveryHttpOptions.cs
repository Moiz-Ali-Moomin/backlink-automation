namespace BacklinkStudio.Infrastructure.Http;

public sealed class DiscoveryHttpOptions
{
    public const string SectionName = "DiscoveryHttp";
    public int TimeoutSeconds { get; init; } = 20;
    public int MaximumResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int MaximumRedirects { get; init; } = 5;
    public string UserAgent { get; init; } = "BacklinkStudio-Discovery/0.3 (+authorized SEO discovery)";
}

public sealed class SerperOptions
{
    public const string SectionName = "Serper";
    public string Endpoint { get; init; } = "https://google.serper.dev/search";
    public string? ApiKey { get; init; }
}
