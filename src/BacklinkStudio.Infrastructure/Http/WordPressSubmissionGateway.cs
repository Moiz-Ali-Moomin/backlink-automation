using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class WordPressSubmissionGateway(
    HttpClient httpClient,
    IWordPressSiteProfileRepository profiles,
    IOptions<WordPressSubmissionOptions> options) : IWordPressSubmissionGateway
{
    private readonly WordPressSubmissionOptions _options = options.Value;
    private readonly HashSet<string> _insecureControlledHosts = options.Value.AllowedInsecureControlledHttpHosts
        .Select(x => new UriBuilder(Uri.UriSchemeHttps, x.Trim().TrimEnd('.')).Uri.IdnHost.ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);

    public async Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken)
    {
        var source = request.Source;
        if (!request.EffectiveOwnershipAuthorized || !OwnedNetworkExecutionEligibility.IsTechnicallyExecutable(source))
            return Failure(WordPressSubmissionMode.ManualActionRequired, SubmissionStatus.ManualActionRequired,
                SubmissionFailureKind.PolicyRejected, "The source is not an enabled, compatible, approved WordPress comment source.");
        var siteProfile = source.OwnedNetworkProfileId is { } profileId
            ? await profiles.FindForSourceAsync(profileId, source.Host, cancellationToken)
            : null;
        if (siteProfile is not null && siteProfile.SubmissionMode is WordPressSubmissionMode.DirectApi or WordPressSubmissionMode.AuthenticatedIntegration)
            return await SubmitDirectAsync(request, siteProfile, cancellationToken);
        if (source.RequiresAuthentication || source.RequiresBrowser || source.RequiresManualAction)
            return Failure(WordPressSubmissionMode.ManualActionRequired, SubmissionStatus.ManualActionRequired,
                SubmissionFailureKind.LoginRequired, "The source requires an authenticated or browser-assisted strategy that is not configured.");
        return await SubmitStandardAsync(request, cancellationToken);
    }

    private async Task<OwnedWordPressSubmissionResult> SubmitDirectAsync(OwnedWordPressSubmissionRequest request,
        WordPressSiteProfile profile, CancellationToken cancellationToken)
    {
        if (profile.CredentialReference is null || !_options.Credentials.TryGetValue(profile.CredentialReference, out var credential))
            return Failure(profile.SubmissionMode, SubmissionStatus.Failed, SubmissionFailureKind.AuthorizationDenied,
                "The named WordPress credential reference is unavailable.");
        var endpoint = RequireAllowedEndpoint(DirectCommentsEndpoint(profile.ApiBaseUrl).AbsoluteUri, profile.Domain);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credential.Username}:{credential.ApplicationPassword}")));
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["post"] = request.Source.PostId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["author_name"] = request.DisplayName,
            ["author_email"] = request.Email,
            ["author_url"] = request.Website ?? string.Empty,
            ["content"] = request.Comment
        });
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await ReadBoundedTextAsync(response.Content, cancellationToken);
        return ParseDirect(response, body, profile.SubmissionMode);
    }

    private async Task<OwnedWordPressSubmissionResult> SubmitStandardAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken)
    {
        // Validation may have followed a canonical redirect (for example WordPress' trailing-slash
        // redirect). Refresh the exact validated page so a harmless redirect is not misclassified as
        // a permanent submission failure. The host check below still pins execution to the approved
        // source host.
        var sourceUri = RequireAllowedEndpoint(request.Source.FinalUrl ?? request.Source.NormalizedUrl, request.Source.Host);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? cookieHeader = null;
        using (var pageRequest = new HttpRequestMessage(HttpMethod.Get, sourceUri))
        {
            pageRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var pageResponse = await httpClient.SendAsync(pageRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!pageResponse.IsSuccessStatusCode)
                return ClassifyFailure(WordPressSubmissionMode.StandardComment, pageResponse.StatusCode,
                    "The WordPress source page could not be refreshed before submission.");
            var html = await ReadBoundedTextAsync(pageResponse.Content, cancellationToken);
            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(html, cancellationToken);
            var pageText = Collapse(document.Body?.TextContent ?? string.Empty);
            if (Contains(pageText, "comments are closed") || Contains(pageText, "comments closed"))
                return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Failed,
                    SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.");
            if (Contains(pageText, "must be logged in") || Contains(pageText, "login to comment"))
                return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.ManualActionRequired,
                    SubmissionFailureKind.LoginRequired, "WordPress requires login before commenting.");
            var form = FindCommentForm(document, request.Source.CommentContentField);
            if (form is null)
                return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Failed,
                    SubmissionFailureKind.UnsupportedForm,
                    "The refreshed page no longer exposes the statically recognized WordPress comment form.");
            foreach (var control in form.QuerySelectorAll("input[name]").Where(x =>
                x.GetAttribute("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true))
            {
                var name = control.GetAttribute("name");
                if (!string.IsNullOrWhiteSpace(name)) values[name] = control.GetAttribute("value") ?? string.Empty;
            }
            cookieHeader = BuildCookieHeader(pageResponse, sourceUri);
        }

        values[request.Source.CommentAuthorField ?? "author"] = request.DisplayName;
        values[request.Source.CommentEmailField ?? "email"] = request.Email;
        values[request.Source.CommentContentField ?? "comment"] = request.Comment;
        values[request.Source.CommentPostIdField ?? "comment_post_ID"] = request.Source.PostId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(request.Website)) values[request.Source.CommentWebsiteField ?? "url"] = request.Website;
        var endpoint = RequireAllowedEndpoint(request.Source.CommentEndpoint ?? request.Source.DetectedFormAction
            ?? throw new InvalidOperationException("The validated comment endpoint is unavailable."), request.Source.Host);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        message.Headers.Referrer = sourceUri;
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
        if (!string.IsNullOrEmpty(cookieHeader)) message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await ReadBoundedTextAsync(response.Content, cancellationToken);
        return ParseStandard(response, body);
    }

    private static OwnedWordPressSubmissionResult ParseDirect(HttpResponseMessage response, string body, WordPressSubmissionMode strategy)
    {
        if (response.StatusCode == HttpStatusCode.Created)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var reference = root.TryGetProperty("id", out var id) ? id.ToString() : null;
                var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
                var pending = status is "hold" or "unapproved" or "pending";
                return new(pending ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted,
                    pending ? ModerationStatus.Pending : ModerationStatus.Approved, strategy, (int)response.StatusCode,
                    reference, SubmissionFailureKind.None, null);
            }
            catch (JsonException)
            {
                return new(SubmissionStatus.Submitted, ModerationStatus.Unknown, strategy, (int)response.StatusCode,
                    null, SubmissionFailureKind.None, null);
            }
        }
        var code = TryReadWordPressCode(body);
        if (code is "comment_duplicate") return Failure(strategy, SubmissionStatus.Duplicate, SubmissionFailureKind.Duplicate, "WordPress rejected a duplicate comment.", response);
        if (code is "comments_closed") return Failure(strategy, SubmissionStatus.Rejected, SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", response);
        if (code is "rest_comment_invalid_post_id" or "comment_invalid_post_ID") return Failure(strategy, SubmissionStatus.Rejected, SubmissionFailureKind.InvalidPost, "WordPress rejected the post identifier.", response);
        return ClassifyFailure(strategy, response.StatusCode, "The WordPress API rejected the comment.");
    }

    private static OwnedWordPressSubmissionResult ParseStandard(HttpResponseMessage response, string body)
    {
        var normalized = Collapse(body);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Failed, SubmissionFailureKind.RateLimited, "WordPress rate limited the comment request.", response);
        if ((int)response.StatusCode >= 500)
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Failed, SubmissionFailureKind.Temporary, "WordPress is temporarily unavailable.", response);
        if (Contains(normalized, "duplicate comment"))
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Duplicate, SubmissionFailureKind.Duplicate, "WordPress rejected a duplicate comment.", response);
        if (Contains(normalized, "comments are closed") || Contains(normalized, "comments closed"))
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Rejected, SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", response);
        if (Contains(normalized, "must be logged in"))
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.ManualActionRequired, SubmissionFailureKind.LoginRequired, "WordPress requires login before commenting.", response);
        if (Contains(normalized, "invalid post") || Contains(normalized, "comment_post_ID"))
            return Failure(WordPressSubmissionMode.StandardComment, SubmissionStatus.Rejected, SubmissionFailureKind.InvalidPost, "WordPress rejected the post identifier.", response);
        if (!response.IsSuccessStatusCode && !IsRedirect(response.StatusCode))
            return ClassifyFailure(WordPressSubmissionMode.StandardComment, response.StatusCode, "WordPress rejected the comment.");
        var location = response.Headers.Location?.OriginalString;
        var reference = ExtractCommentReference(location);
        var pending = Contains(normalized, "awaiting moderation") || Contains(normalized, "held for moderation") ||
            Contains(normalized, "pending moderation") || Contains(location ?? string.Empty, "unapproved=") ||
            Contains(location ?? string.Empty, "moderation");
        return new(pending ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted,
            pending ? ModerationStatus.Pending : reference is null ? ModerationStatus.Unknown : ModerationStatus.Approved,
            WordPressSubmissionMode.StandardComment,
            (int)response.StatusCode, reference, SubmissionFailureKind.None, null);
    }

    private static OwnedWordPressSubmissionResult ClassifyFailure(WordPressSubmissionMode strategy, HttpStatusCode status, string message)
    {
        var kind = status == HttpStatusCode.TooManyRequests ? SubmissionFailureKind.RateLimited
            : (int)status >= 500 || status == HttpStatusCode.RequestTimeout ? SubmissionFailureKind.Temporary
            : status == HttpStatusCode.Unauthorized ? SubmissionFailureKind.LoginRequired
            : status == HttpStatusCode.Forbidden ? SubmissionFailureKind.Permanent
            : SubmissionFailureKind.Permanent;
        var result = status == HttpStatusCode.Forbidden ? SubmissionStatus.Rejected : SubmissionStatus.Failed;
        return Failure(strategy, result, kind, message, httpStatus: (int)status);
    }

    private static OwnedWordPressSubmissionResult Failure(WordPressSubmissionMode strategy, SubmissionStatus status,
        SubmissionFailureKind kind, string message, HttpResponseMessage? response = null, int? httpStatus = null) =>
        new(status, ModerationStatus.Unknown, strategy, httpStatus ?? (response is null ? null : (int)response.StatusCode),
            null, kind, message, RetryAfter: RetryAfter(response));

    private static TimeSpan? RetryAfter(HttpResponseMessage? response) =>
        response?.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromDays(1)
            ? delay
            : null;

    private Uri RequireAllowedEndpoint(string value, string expectedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp &&
                ((_options.AllowInsecureLoopbackHttpForTesting && uri.IsLoopback) || _insecureControlledHosts.Contains(uri.IdnHost.ToLowerInvariant())))) ||
            !string.Equals(uri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new UnauthorizedAccessException("WordPress submission endpoint does not match the approved source host and transport policy.");
        return uri;
    }

    private static Uri DirectCommentsEndpoint(string baseUrl)
    {
        var root = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");
        return root.AbsolutePath.Contains("/wp-json/", StringComparison.OrdinalIgnoreCase)
            ? new Uri(root, "comments") : new Uri(root, "wp-json/wp/v2/comments");
    }

    private async Task<string> ReadBoundedTextAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _options.MaximumResponseBytes)
            throw new InvalidDataException("WordPress response exceeds the configured size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_options.MaximumResponseBytes, 64 * 1_024));
        var buffer = new byte[16 * 1_024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _options.MaximumResponseBytes)
                throw new InvalidDataException("WordPress response exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static IElement? FindCommentForm(IDocument document, string? resolvedCommentField) =>
        document.QuerySelector("form#commentform")
        ?? document.QuerySelector("form[action*='wp-comments-post.php' i]")
        ?? document.QuerySelectorAll("form").Take(64).FirstOrDefault(form => form.QuerySelectorAll("textarea[name]")
            .Take(128).Any(textarea => string.Equals(textarea.GetAttribute("name"), resolvedCommentField,
                    StringComparison.Ordinal) ||
                CommentAliases.Contains(textarea.GetAttribute("name"), StringComparer.OrdinalIgnoreCase)));
    private static readonly string[] CommentAliases = ["comment", "message", "body", "content"];
    private static string? BuildCookieHeader(HttpResponseMessage response, Uri source)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var container = new CookieContainer();
        foreach (var cookie in cookies)
        {
            try { container.SetCookies(source, cookie); }
            catch (CookieException) { }
        }
        return container.GetCookieHeader(source) is { Length: > 0 } value ? value : null;
    }
    private static string? TryReadWordPressCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
    private static string? ExtractCommentReference(string? location)
    {
        if (location is null) return null;
        var marker = location.LastIndexOf("#comment-", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? null : location[(marker + "#comment-".Length)..].Split('&', '?')[0];
    }
    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static bool Contains(string value, string expected) => value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
