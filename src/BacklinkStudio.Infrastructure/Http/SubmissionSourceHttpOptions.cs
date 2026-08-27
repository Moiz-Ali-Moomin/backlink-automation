namespace BacklinkStudio.Infrastructure.Http;

public sealed class SubmissionSourceHttpOptions
{
    public const string SectionName = "SubmissionSourceHttp";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaximumResponseBytes { get; set; } = 2 * 1_024 * 1_024;
    public int MaximumRedirects { get; set; } = 5;
    public string UserAgent { get; set; } = "BacklinkStudio-SourceValidator/1.0";
    public string[] AllowedPrivateHosts { get; set; } = [];
}
