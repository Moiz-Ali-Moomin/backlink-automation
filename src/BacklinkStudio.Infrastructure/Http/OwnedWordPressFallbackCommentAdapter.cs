using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class OwnedWordPressFallbackCommentAdapter(
    HttpClient httpClient,
    IOptions<WordPressSubmissionOptions> submissionOptions,
    IOptions<WordPressFallbackOptions> fallbackOptions) : IOwnedWordPressFallbackCommentAdapter
{
    private const int AbsoluteMaximumStrategies = 4;
    private readonly WordPressSubmissionOptions _submission = submissionOptions.Value;
    private readonly WordPressFallbackOptions _fallback = fallbackOptions.Value;
    private readonly HashSet<string> _insecureHosts = submissionOptions.Value.AllowedInsecureControlledHttpHosts
        .Select(NormalizeHost).ToHashSet(StringComparer.Ordinal);

    public string Name => nameof(OwnedWordPressFallbackCommentAdapter);

    public async Task<OwnedWordPressSubmissionResult> SubmitAsync(
        OwnedWordPressSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var source = request.Source;
        if (!request.EffectiveOwnershipAuthorized || !source.Enabled ||
            source.ValidationStatus != SubmissionSourceValidationStatus.Valid ||
            source.TechnicalCompatibility is not (TechnicalCompatibility.Compatible or TechnicalCompatibility.FallbackCandidate) ||
            source.CmsType != CmsType.WordPress || source.PostId is null ||
            source.RequiresAuthentication || source.RequiresManualAction)
        {
            return Failure(SubmissionFailureKind.PolicyRejected,
                "The source is not an authorized WordPress fallback candidate.");
        }

        if (source.AdditionalRequiredFields.Length > 0)
            return Failure(SubmissionFailureKind.UnsupportedForm,
                "The fallback form contains required fields that the bounded adapter cannot populate.");

        var pageUri = RequireAllowed(source.FinalUrl ?? source.NormalizedUrl, source.Host);
        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, pageUri);
        pageRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var pageResponse = await httpClient.SendAsync(pageRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!pageResponse.IsSuccessStatusCode)
            return ClassifyHttp(pageResponse.StatusCode, "The fallback source page could not be refreshed.", pageUri.AbsoluteUri);

        string html;
        try
        {
            html = await ReadBoundedAsync(pageResponse.Content, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Failure(SubmissionFailureKind.BrowserRequired,
                "The fallback page exceeds the bounded static inspection limit; controlled browser execution is required.");
        }
        var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
        var markup = document.DocumentElement?.OuterHtml ?? string.Empty;
        var text = Collapse(document.Body?.TextContent ?? string.Empty);
        if (!HasWordPressEvidence(markup, document))
            return Failure(SubmissionFailureKind.ValidationFailed, "WordPress evidence is no longer present.");
        if (Contains(markup, "captcha") || Contains(markup, "recaptcha") || Contains(markup, "hcaptcha"))
            return Failure(SubmissionFailureKind.PolicyRejected, "CAPTCHA requires manual action and is not bypassed.");
        if (Contains(text, "must be logged in"))
            return Failure(SubmissionFailureKind.LoginRequired, "WordPress requires authentication before commenting.");
        if (Contains(text, "comments are closed") || Contains(text, "comments closed"))
            return Failure(SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", SubmissionStatus.Rejected);

        var pagePostId = FindPostId(document);
        if (pagePostId is null || pagePostId != source.PostId)
            return Failure(SubmissionFailureKind.ValidationFailed,
                "The validated WordPress post identifier is not present in the refreshed page evidence.");

        var strategies = BuildStrategies(document, pageUri, source)
            .Take(Math.Min(_fallback.MaximumStrategies, AbsoluteMaximumStrategies)).ToArray();
        if (strategies.Length == 0)
            return Failure(SubmissionFailureKind.EndpointNotFound, "No bounded same-origin WordPress fallback endpoint is available.");

        foreach (var strategy in strategies)
        {
            var values = HiddenValues((IParentNode?)strategy.Form ?? document);
            values[source.CommentAuthorField ?? "author"] = request.DisplayName;
            values[source.CommentEmailField ?? "email"] = request.Email;
            values[source.CommentContentField ?? "comment"] = request.Comment;
            values[source.CommentPostIdField ?? "comment_post_ID"] = source.PostId.Value.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(request.Website)) values[source.CommentWebsiteField ?? "url"] = request.Website;

            using var message = new HttpRequestMessage(HttpMethod.Post, strategy.Endpoint)
            {
                Content = new FormUrlEncodedContent(values)
            };
            message.Headers.Referrer = pageUri;
            message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadBoundedAsync(response.Content, cancellationToken);
            var redirect = response.Headers.Location is null ? null :
                (response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(strategy.Endpoint, response.Headers.Location));
            if (redirect is not null && !SameOrigin(redirect, pageUri))
                return Failure(SubmissionFailureKind.AuthorizationDenied,
                    "WordPress fallback redirected to an unauthorized host.", endpoint: strategy.Endpoint.AbsoluteUri,
                    redirect: redirect.AbsoluteUri);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            return Parse(response, body, strategy.Endpoint.AbsoluteUri, redirect?.AbsoluteUri);
        }

        return Failure(SubmissionFailureKind.EndpointNotFound,
            "The bounded WordPress fallback endpoints were not found.");
    }

    private List<FallbackStrategy> BuildStrategies(IDocument document, Uri pageUri, SubmissionSource source)
    {
        var broadForm = document.QuerySelectorAll("form").Take(64).FirstOrDefault(form =>
        {
            var hasComment = form.QuerySelectorAll("textarea[name]").Take(128).Any(textarea =>
                string.Equals(textarea.GetAttribute("name"), source.CommentContentField, StringComparison.Ordinal) ||
                CommentAliases.Contains(textarea.GetAttribute("name"), StringComparer.OrdinalIgnoreCase));
            var hasPost = form.QuerySelectorAll("input[name]").Take(128).Any(input =>
                string.Equals(input.GetAttribute("name"), source.CommentPostIdField, StringComparison.Ordinal) ||
                PostIdAliases.Contains(input.GetAttribute("name"), StringComparer.OrdinalIgnoreCase));
            var action = form.GetAttribute("action");
            return hasComment && (hasPost || action?.Contains("wp-comments-post.php",
                StringComparison.OrdinalIgnoreCase) == true);
        });
        var strategies = new List<FallbackStrategy>(AbsoluteMaximumStrategies);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Add(broadForm?.GetAttribute("action"), broadForm);
        Add(source.DetectedFormAction, broadForm);
        Add(source.CommentEndpoint, null);
        Add(new Uri(pageUri, "/wp-comments-post.php").AbsoluteUri, null);
        return strategies;

        void Add(string? candidate, IElement? form)
        {
            if (string.IsNullOrWhiteSpace(candidate) || strategies.Count == AbsoluteMaximumStrategies) return;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint) &&
                !Uri.TryCreate(pageUri, candidate, out endpoint)) return;
            if (!SameOrigin(endpoint, pageUri) ||
                !string.Equals(endpoint.IdnHost, source.Host, StringComparison.OrdinalIgnoreCase)) return;
            var allowed = RequireAllowed(endpoint.AbsoluteUri, source.Host);
            if (seen.Add(allowed.AbsoluteUri)) strategies.Add(new(allowed, form));
        }
    }

    private static OwnedWordPressSubmissionResult Parse(HttpResponseMessage response, string body, string endpoint, string? redirect)
    {
        var normalized = Collapse(body);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return Failure(SubmissionFailureKind.RateLimited, "WordPress rate limited the fallback request.",
                endpoint: endpoint, redirect: redirect, httpStatus: (int)response.StatusCode,
                retryAfter: RetryAfter(response));
        if ((int)response.StatusCode >= 500)
            return Failure(SubmissionFailureKind.Temporary, "WordPress is temporarily unavailable.", endpoint: endpoint, redirect: redirect, httpStatus: (int)response.StatusCode);
        if (Contains(normalized, "duplicate comment"))
            return Failure(SubmissionFailureKind.Duplicate, "WordPress rejected a duplicate comment.", SubmissionStatus.Duplicate, endpoint, redirect);
        if (Contains(normalized, "comments are closed") || Contains(normalized, "comments closed"))
            return Failure(SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", SubmissionStatus.Rejected, endpoint, redirect);
        if (Contains(normalized, "must be logged in"))
            return Failure(SubmissionFailureKind.LoginRequired, "WordPress requires authentication before commenting.", SubmissionStatus.ManualActionRequired, endpoint, redirect);
        if (!response.IsSuccessStatusCode && !IsRedirect(response.StatusCode))
            return ClassifyHttp(response.StatusCode, "WordPress rejected the fallback comment.", endpoint, redirect);
        var pending = Contains(normalized, "awaiting moderation") || Contains(normalized, "pending moderation") ||
            Contains(redirect ?? string.Empty, "unapproved=") || Contains(redirect ?? string.Empty, "moderation");
        return new(pending ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted,
            pending ? ModerationStatus.Pending : ModerationStatus.Unknown, WordPressSubmissionMode.FallbackComment,
            (int)response.StatusCode, ExtractCommentReference(redirect), SubmissionFailureKind.None, null, endpoint, redirect);
    }

    private static OwnedWordPressSubmissionResult ClassifyHttp(HttpStatusCode status, string message, string endpoint, string? redirect = null)
    {
        var kind = status == HttpStatusCode.TooManyRequests ? SubmissionFailureKind.RateLimited
            : (int)status >= 500 || status == HttpStatusCode.RequestTimeout ? SubmissionFailureKind.Temporary
            : status == HttpStatusCode.Unauthorized ? SubmissionFailureKind.LoginRequired
            : status == HttpStatusCode.Forbidden ? SubmissionFailureKind.Permanent
            : status == HttpStatusCode.NotFound ? SubmissionFailureKind.EndpointNotFound : SubmissionFailureKind.Permanent;
        var result = status == HttpStatusCode.Unauthorized ? SubmissionStatus.ManualActionRequired
            : status == HttpStatusCode.Forbidden ? SubmissionStatus.Rejected : SubmissionStatus.Failed;
        return Failure(kind, message, result, endpoint, redirect, (int)status);
    }

    private static OwnedWordPressSubmissionResult Failure(SubmissionFailureKind kind, string message,
        SubmissionStatus status = SubmissionStatus.Failed, string? endpoint = null, string? redirect = null,
        int? httpStatus = null, TimeSpan? retryAfter = null) =>
        new(status, ModerationStatus.Unknown, WordPressSubmissionMode.FallbackComment, httpStatus, null, kind,
            message, endpoint, redirect, retryAfter);

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromDays(1)
            ? delay
            : null;

    private Uri RequireAllowed(string value, string expectedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(NormalizeHost(uri.IdnHost), NormalizeHost(expectedHost), StringComparison.Ordinal) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp &&
                ((_submission.AllowInsecureLoopbackHttpForTesting && uri.IsLoopback) || _insecureHosts.Contains(NormalizeHost(uri.IdnHost))))))
            throw new UnauthorizedAccessException("Fallback endpoint does not match the authorized host and transport policy.");
        return uri;
    }

    private async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _submission.MaximumResponseBytes)
            throw new InvalidDataException("WordPress fallback response exceeds the configured size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_submission.MaximumResponseBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _submission.MaximumResponseBytes)
                throw new InvalidDataException("WordPress fallback response exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static Dictionary<string, string> HiddenValues(IParentNode root) => root.QuerySelectorAll("input[name]")
        .Where(x => x.GetAttribute("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true)
        .Where(x => !string.IsNullOrWhiteSpace(x.GetAttribute("name")))
        .Take(32).ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value") ?? string.Empty, StringComparer.Ordinal);
    private static bool HasWordPressEvidence(string markup, IDocument document) =>
        Contains(markup, "wp-content") || Contains(markup, "wp-includes") || Contains(markup, "wp-json") ||
        Contains(markup, "wp-comments-post.php") || document.QuerySelector("meta[name='generator'][content*='WordPress' i]") is not null;
    private static long? FindPostId(IDocument document)
    {
        var value = document.QuerySelector("input[name='comment_post_ID'],input[name='comment_post_id'],input[name='post_id'],[data-post-id]")
            ?.GetAttribute("value") ?? document.QuerySelector("[data-post-id]")?.GetAttribute("data-post-id");
        if (long.TryParse(value, out var parsed) && parsed > 0) return parsed;
        foreach (var element in document.QuerySelectorAll("[id],[class]"))
        {
            foreach (var token in $"{element.Id} {element.ClassName}".Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = token.StartsWith("postid-", StringComparison.OrdinalIgnoreCase) ? token[7..]
                    : token.StartsWith("post-", StringComparison.OrdinalIgnoreCase) ? token[5..] : null;
                if (long.TryParse(candidate, out parsed) && parsed > 0) return parsed;
            }
        }
        return null;
    }
    private static string NormalizeHost(string value) => new UriBuilder(Uri.UriSchemeHttps, value.Trim().TrimEnd('.')).Uri.IdnHost.ToLowerInvariant();
    private static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme && left.Port == right.Port &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase);
    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static string? ExtractCommentReference(string? location)
    {
        if (location is null) return null;
        var marker = location.LastIndexOf("#comment-", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? null : location[(marker + 9)..].Split('&', '?')[0];
    }
    private static bool Contains(string value, string expected) => value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static readonly string[] CommentAliases = ["comment", "message", "body", "content"];
    private static readonly string[] PostIdAliases = ["comment_post_ID", "comment_post_id", "post_id", "postId"];
    private sealed record FallbackStrategy(Uri Endpoint, IElement? Form);
}
