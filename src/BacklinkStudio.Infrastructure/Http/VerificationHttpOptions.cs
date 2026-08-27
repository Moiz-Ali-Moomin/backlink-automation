namespace BacklinkStudio.Infrastructure.Http;

public sealed class VerificationHttpOptions
{
    public const string SectionName = "VerificationHttp";
    public int TimeoutSeconds { get; init; } = 20;
    public int MaximumResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int MaximumRedirects { get; init; } = 5;
    public string UserAgent { get; init; } = "BacklinkStudio-Verifier/0.6 (+authorized backlink verification)";
    public string[] AllowedPrivateHosts { get; init; } = [];
}
