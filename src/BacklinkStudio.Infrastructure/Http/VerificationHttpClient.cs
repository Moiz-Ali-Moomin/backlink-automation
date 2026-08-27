using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BacklinkStudio.Application;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class VerificationHttpClient(
    HttpClient httpClient,
    IOptions<VerificationHttpOptions> options,
    IUrlNormalizer urlNormalizer,
    TimeProvider timeProvider) : IBacklinkVerifier
{
    private readonly VerificationHttpOptions _options = options.Value;

    public async Task<BacklinkVerificationResult> VerifyAsync(Uri sourceUrl, string normalizedTargetUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        var target = urlNormalizer.Normalize(normalizedTargetUrl);
        if (!target.IsValid) throw new ArgumentException("The normalized target URL is invalid.", nameof(normalizedTargetUrl));

        var started = Stopwatch.GetTimestamp();
        var current = sourceUrl;
        try
        {
            for (var redirect = 0; redirect <= _options.MaximumRedirects; redirect++)
            {
                var source = urlNormalizer.Normalize(current.AbsoluteUri);
                if (!source.IsValid) return Error(sourceUrl, current, null, "Verification source URL is invalid.", started);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == _options.MaximumRedirects || response.Headers.Location is null)
                    {
                        return Error(sourceUrl, current, (int)response.StatusCode, "Redirect limit exceeded or redirect location missing.", started);
                    }
                    current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                    continue;
                }

                var status = (int)response.StatusCode;
                if ((int)response.StatusCode is 429 or >= 500)
                {
                    return Error(sourceUrl, current, status, "Verification source returned a transient HTTP failure.", started);
                }

                if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound and not HttpStatusCode.Gone)
                {
                    return Error(sourceUrl, current, status, "Verification source returned a non-success HTTP status.", started);
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ||
                    contentType is null || (!contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase) && !contentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
                {
                    return Result(sourceUrl, current, false, status, null, [], null, null, started);
                }

                var html = await ReadBoundedAsync(response.Content, cancellationToken);
                var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
                var match = document.QuerySelectorAll("a[href]")
                    .Select(anchor => Match(anchor, current, target.NormalizedUrl!))
                    .FirstOrDefault(value => value is not null);
                var canonical = document.QuerySelectorAll("link[href]")
                    .FirstOrDefault(link => ParseTokens(link.GetAttribute("rel")).Contains("canonical", StringComparer.Ordinal))
                    ?.GetAttribute("href");
                var canonicalResult = urlNormalizer.Normalize(canonical, current);
                return Result(
                    sourceUrl,
                    current,
                    match is not null,
                    status,
                    match?.Anchor,
                    match?.Rel ?? [],
                    canonicalResult.IsValid ? canonicalResult.NormalizedUrl : null,
                    null,
                    started);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(sourceUrl, current, null, "Verification request timed out.", started);
        }
        catch (HttpRequestException)
        {
            return Error(sourceUrl, current, null, "Verification request failed.", started);
        }
        catch (InvalidDataException exception)
        {
            return Error(sourceUrl, current, null, exception.Message, started);
        }

        return Error(sourceUrl, current, null, "Verification request failed.", started);
    }

    private LinkMatch? Match(IElement anchor, Uri baseUri, string normalizedTargetUrl)
    {
        var href = urlNormalizer.Normalize(anchor.GetAttribute("href"), baseUri);
        if (!href.IsValid || !string.Equals(href.NormalizedUrl, normalizedTargetUrl, StringComparison.Ordinal)) return null;
        return new LinkMatch(Limit(Collapse(anchor.TextContent), 500), ParseTokens(anchor.GetAttribute("rel")));
    }

    private async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _options.MaximumResponseBytes) throw new InvalidDataException("Response exceeds the configured verification size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_options.MaximumResponseBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _options.MaximumResponseBytes) throw new InvalidDataException("Response exceeds the configured verification size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private BacklinkVerificationResult Error(Uri original, Uri final, int? status, string error, long started) =>
        Result(original, final, false, status, null, [], null, error, started);

    private BacklinkVerificationResult Result(Uri original, Uri final, bool found, int? status, string? anchor, IReadOnlyList<string> rel, string? canonical, string? error, long started) =>
        new(original.AbsoluteUri, final.AbsoluteUri, found, status, anchor, rel, canonical, timeProvider.GetUtcNow(), error, Stopwatch.GetElapsedTime(started));

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static string[] ParseTokens(string? value) => value?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Limit(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximum ? value : value[..maximum];
    private sealed record LinkMatch(string? Anchor, string[] Rel);
}
