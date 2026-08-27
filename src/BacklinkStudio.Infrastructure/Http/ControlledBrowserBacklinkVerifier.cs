using System.Diagnostics;
using BacklinkStudio.Application;
using Microsoft.Playwright;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class ControlledBrowserBacklinkVerifier(
    ControlledBrowserRuntime runtime,
    IUrlNormalizer urlNormalizer,
    TimeProvider timeProvider) : IControlledBrowserBacklinkVerifier
{
    private const int MaximumMatchingAnchors = 32;
    private const int MaximumFrames = 16;

    public Task<BacklinkVerificationResult> VerifyAsync(Uri sourceUrl, string expectedHost,
        string normalizedTargetUrl, CancellationToken cancellationToken) =>
        runtime.UsePageAsync(sourceUrl, expectedHost,
            page => InspectAsync(page, sourceUrl, expectedHost, normalizedTargetUrl, cancellationToken),
            cancellationToken);

    private async Task<BacklinkVerificationResult> InspectAsync(IPage page, Uri original, string expectedHost,
        string normalizedTargetUrl, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var target = urlNormalizer.Normalize(normalizedTargetUrl);
        var final = new Uri(page.Url, UriKind.Absolute);
        if (!target.IsValid)
            return Result(original, final, false, null, [], null, "The normalized target URL is invalid.", started);

        foreach (var frame in page.Frames.Take(MaximumFrames))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(frame.Url, UriKind.Absolute, out var frameUri) ||
                !string.Equals(frameUri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase))
                continue;
            var normalizedTarget = target.NormalizedUrl!;
            var escapedTarget = EscapeCssString(normalizedTarget);
            var alternateTarget = normalizedTarget[^1] == '/'
                ? EscapeCssString(normalizedTarget.TrimEnd('/'))
                : escapedTarget;
            var anchors = frame.Locator($"a[href=\"{escapedTarget}\"],a[href^=\"{escapedTarget}#\"]," +
                $"a[href=\"{alternateTarget}\"],a[href^=\"{alternateTarget}#\"]");
            var count = Math.Min(await anchors.CountAsync(), MaximumMatchingAnchors);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var anchor = anchors.Nth(index);
                var href = urlNormalizer.Normalize(await anchor.GetAttributeAsync("href"), frameUri);
                if (!href.IsValid || !string.Equals(href.NormalizedUrl, target.NormalizedUrl, StringComparison.Ordinal))
                    continue;
                var textValues = await anchor.AllTextContentsAsync();
                var text = Collapse(textValues.Count == 0 ? null : textValues[0]);
                return Result(original, final, true, Limit(text, 500),
                    ParseTokens(await anchor.GetAttributeAsync("rel")), await CanonicalAsync(frame, frameUri), null, started);
            }
        }

        return Result(original, final, false, null, [], null, null, started);
    }

    private async Task<string?> CanonicalAsync(IFrame frame, Uri baseUri)
    {
        var canonical = frame.Locator("link[rel~='canonical' i][href]").First;
        if (await canonical.CountAsync() == 0) return null;
        var value = urlNormalizer.Normalize(await canonical.GetAttributeAsync("href"), baseUri);
        return value.IsValid ? value.NormalizedUrl : null;
    }

    private BacklinkVerificationResult Result(Uri original, Uri final, bool found, string? anchor,
        IReadOnlyList<string> rel, string? canonical, string? error, long started) =>
        new(original.AbsoluteUri, final.AbsoluteUri, found, null, anchor, rel, canonical,
            timeProvider.GetUtcNow(), error, Stopwatch.GetElapsedTime(started));

    private static string[] ParseTokens(string? value) => value?
        .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private static string Collapse(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Limit(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximum ? value : value[..maximum];
    private static string EscapeCssString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
