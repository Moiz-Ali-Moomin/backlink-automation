namespace BacklinkStudio.Infrastructure.Http;

public sealed class AnalysisHttpOptions
{
    public const string SectionName = "AnalysisHttp";
    public int TimeoutSeconds { get; init; } = 20;
    public int MaximumResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int MaximumRedirects { get; init; } = 5;
    public string UserAgent { get; init; } = "BacklinkStudio-Analyzer/0.2 (+authorized SEO analysis)";
}
