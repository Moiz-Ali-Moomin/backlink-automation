using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Html.Parser;
using BacklinkStudio.Application;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class AnalysisHttpClient(
    HttpClient httpClient,
    IOptions<AnalysisHttpOptions> options,
    IUrlNormalizer urlNormalizer,
    IEnumerable<ICmsDetector> cmsDetectors,
    TimeProvider timeProvider) : ISiteAnalyzer
{
    private readonly AnalysisHttpOptions _options = options.Value;

    public async Task<SiteAnalysisResult> AnalyzeAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var started = Stopwatch.GetTimestamp();
        var current = url;
        try
        {
            for (var redirect = 0; redirect <= _options.MaximumRedirects; redirect++)
            {
                var normalized = urlNormalizer.Normalize(current.AbsoluteUri);
                if (!normalized.IsValid)
                {
                    return Error(url, current, "Analysis URL is invalid.", started);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == _options.MaximumRedirects || response.Headers.Location is null)
                    {
                        return Error(url, current, "Redirect limit exceeded or redirect location missing.", started, (int)response.StatusCode);
                    }
                    current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                    continue;
                }

                return await ParseAsync(url, current, response, started, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(url, current, "Analysis request timed out.", started);
        }
        catch (HttpRequestException)
        {
            return Error(url, current, "Analysis request failed.", started);
        }
        catch (InvalidDataException exception)
        {
            return Error(url, current, exception.Message, started);
        }

        return Error(url, current, "Analysis request failed.", started);
    }

    private async Task<SiteAnalysisResult> ParseAsync(Uri original, Uri final, HttpResponseMessage response, long started, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || (!contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase) && !contentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
        {
            return Error(original, final, "Response is not HTML.", started, (int)response.StatusCode, contentType);
        }

        var html = await ReadBoundedAsync(response.Content, cancellationToken);
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var links = document.QuerySelectorAll("a[href]")
            .Select(anchor => new { Result = urlNormalizer.Normalize(anchor.GetAttribute("href"), final), Anchor = Collapse(anchor.TextContent), Rel = ParseTokens(anchor.GetAttribute("rel")) })
            .Where(x => x.Result.IsValid)
            .Select(x => new AnalyzedLink(x.Result.NormalizedUrl!, Limit(x.Anchor, 500) ?? string.Empty, x.Rel))
            .DistinctBy(x => x.NormalizedUrl, StringComparer.Ordinal)
            .Take(10_000)
            .ToArray();
        var canonical = document.QuerySelectorAll("link")
            .FirstOrDefault(x => x.HasAttribute("href") && ParseTokens(x.GetAttribute("rel")).Contains("canonical", StringComparer.Ordinal))
            ?.GetAttribute("href");
        var canonicalResult = urlNormalizer.Normalize(canonical, final);
        var robots = string.Join(',', document.QuerySelectorAll("meta[name]")
            .Where(x => x.GetAttribute("name")?.Equals("robots", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(x => ParseTokens(x.GetAttribute("content"))).Distinct(StringComparer.OrdinalIgnoreCase));
        var assets = document.QuerySelectorAll("script[src],link[href]").Select(x => x.GetAttribute("src") ?? x.GetAttribute("href") ?? string.Empty).Where(x => x.Length > 0).Take(1_000).ToArray();
        var generator = document.QuerySelector("meta[name=generator]")?.GetAttribute("content");
        var markup = document.DocumentElement?.OuterHtml ?? string.Empty;
        var markers = markup.Contains("wp-content", StringComparison.OrdinalIgnoreCase) ? new[] { "wordpress" } : [];
        var cms = cmsDetectors.Select(x => x.Detect(new SiteDocumentSignals(generator, assets, markers))).Where(x => x is not null).OrderByDescending(x => x!.Confidence).FirstOrDefault();
        var signals = DetectSignals(document.Title, markup);
        var text = Collapse(document.Body?.TextContent ?? string.Empty);
        var domain = final.IdnHost.ToLowerInvariant();
        var externalLinks = links.Count(x => !string.Equals(new Uri(x.NormalizedUrl).IdnHost, domain, StringComparison.OrdinalIgnoreCase));
        var requiresJavaScript = document.QuerySelector("noscript")?.TextContent.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true || (text.Length < 40 && document.Scripts.Length > 5);

        return new SiteAnalysisResult(original.AbsoluteUri, final.AbsoluteUri, (int)response.StatusCode, contentType, Limit(document.Title, 500), canonicalResult.IsValid ? canonicalResult.NormalizedUrl : null, domain, cms?.Name, Limit(robots, 500), links, signals, requiresJavaScript, links.Length, externalLinks, CountWords(text), timeProvider.GetUtcNow(), null, Stopwatch.GetElapsedTime(started));
    }

    private async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _options.MaximumResponseBytes)
        {
            throw new InvalidDataException("Response exceeds the configured analysis size limit.");
        }
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_options.MaximumResponseBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > _options.MaximumResponseBytes)
            {
                throw new InvalidDataException("Response exceeds the configured analysis size limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private SiteAnalysisResult Error(Uri original, Uri final, string error, long started, int? status = null, string? contentType = null) =>
        new(original.AbsoluteUri, final.AbsoluteUri, status, contentType, null, null, final.IdnHost.ToLowerInvariant(), null, null, [], [], false, 0, 0, 0, timeProvider.GetUtcNow(), error, Stopwatch.GetElapsedTime(started));

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static string[] ParseTokens(string? value) => value?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Limit(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : Collapse(value).Length <= maximum ? Collapse(value) : Collapse(value)[..maximum];
    private static int CountWords(string value) => value.Length == 0 ? 0 : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<string> DetectSignals(string? title, string markup)
    {
        var text = (title + " " + markup).ToLowerInvariant();
        var signals = new List<string>();
        if (text.Contains("submit listing", StringComparison.Ordinal) || text.Contains("add listing", StringComparison.Ordinal)) signals.Add("directory-listing");
        if (text.Contains("useful resources", StringComparison.Ordinal) || text.Contains("resource links", StringComparison.Ordinal)) signals.Add("resource-page");
        if (text.Contains("our partners", StringComparison.Ordinal) || text.Contains("become a partner", StringComparison.Ordinal)) signals.Add("partner-page");
        if (text.Contains("member profile", StringComparison.Ordinal) || text.Contains("create profile", StringComparison.Ordinal)) signals.Add("profile");
        if (text.Contains("comment-form", StringComparison.Ordinal) || text.Contains("name=\"comment\"", StringComparison.Ordinal)) signals.Add("comment-form");
        return signals;
    }
}
