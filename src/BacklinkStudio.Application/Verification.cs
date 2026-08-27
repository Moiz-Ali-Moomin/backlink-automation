namespace BacklinkStudio.Application;

public sealed record BacklinkVerificationResult(
    string OriginalUrl,
    string FinalUrl,
    bool Found,
    int? HttpStatus,
    string? Anchor,
    IReadOnlyList<string> Rel,
    string? CanonicalUrl,
    DateTimeOffset CheckedAt,
    string? Error,
    TimeSpan Duration);

public interface IBacklinkVerifier
{
    Task<BacklinkVerificationResult> VerifyAsync(Uri sourceUrl, string normalizedTargetUrl, CancellationToken cancellationToken);
}

public interface IControlledBrowserBacklinkVerifier
{
    Task<BacklinkVerificationResult> VerifyAsync(Uri sourceUrl, string expectedHost, string normalizedTargetUrl,
        CancellationToken cancellationToken);
}
