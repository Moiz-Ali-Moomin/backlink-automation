using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class SubmissionSourceHttpInspector(HttpClient httpClient, IOptions<SubmissionSourceHttpOptions> options, IUrlNormalizer urlNormalizer) : ISubmissionSourceInspector
{
    private const int MaximumForms = 64;
    private const int MaximumControls = 128;
    private readonly SubmissionSourceHttpOptions _options = options.Value;

    public Task<SubmissionSourceValidation> InspectAsync(Uri source, CancellationToken cancellationToken) =>
        InspectAsync(source, false, cancellationToken);

    public async Task<SubmissionSourceValidation> InspectAsync(Uri source, bool effectiveOwnershipAuthorized, CancellationToken cancellationToken)
    {
        var current = source;
        var redirects = new List<string>();
        for (var redirect = 0; redirect <= _options.MaximumRedirects; redirect++)
        {
            var normalized = urlNormalizer.Normalize(current.AbsoluteUri);
            if (!normalized.IsValid) return Error("Validation URL is invalid.", current, redirects);
            redirects.Add(normalized.NormalizedUrl!);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirect == _options.MaximumRedirects || response.Headers.Location is null)
                    return Error("Redirect limit exceeded or redirect location missing.", current, redirects, (int)response.StatusCode);
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                continue;
            }
            return await ParseAsync(current, redirects, response, effectiveOwnershipAuthorized, cancellationToken);
        }
        return Error("Validation request failed.", current, redirects);
    }

    private async Task<SubmissionSourceValidation> ParseAsync(Uri final, IReadOnlyList<string> redirects, HttpResponseMessage response,
        bool effectiveOwnershipAuthorized, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var contentLength = response.Content.Headers.ContentLength;
        if (!response.IsSuccessStatusCode)
            return Error($"Source returned HTTP {(int)response.StatusCode}.", final, redirects, (int)response.StatusCode, contentType, contentLength);
        if (contentType is null || (!contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase) && !contentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            return Error("Source response is not HTML.", final, redirects, (int)response.StatusCode, contentType, contentLength);

        byte[] bytes;
        try
        {
            bytes = await ReadBoundedAsync(response.Content, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return ResponseTooLarge(final, redirects, response, effectiveOwnershipAuthorized);
        }
        var html = Encoding.UTF8.GetString(bytes);
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var markup = document.DocumentElement?.OuterHtml ?? string.Empty;
        var lowerMarkup = markup.ToLowerInvariant();
        var generator = document.QuerySelectorAll("meta[name]")
            .FirstOrDefault(x => x.GetAttribute("name")?.Equals("generator", StringComparison.OrdinalIgnoreCase) == true)
            ?.GetAttribute("content");
        var form = FindCommentForm(document);
        var controls = form is null ? [] : form.QuerySelectorAll("input[name],textarea[name],select[name]")
            .Take(MaximumControls).ToArray();
        var pageControls = document.QuerySelectorAll("input[name],textarea[name],select[name]")
            .Take(MaximumControls).ToArray();
        var postControl = FindControl(controls, PostIdAliases)
            ?? FindControl(pageControls, PostIdAliases);
        var postId = ParsePostId(postControl?.GetAttribute("value")) ?? FindRenderedPostId(document);
        var action = form?.GetAttribute("action");
        var endpoint = urlNormalizer.Normalize(action, final);
        var wpSignals = new List<string>();
        if (generator?.Contains("wordpress", StringComparison.OrdinalIgnoreCase) == true) wpSignals.Add("generator metadata");
        if (lowerMarkup.Contains("wp-content", StringComparison.Ordinal)) wpSignals.Add("wp-content asset");
        if (lowerMarkup.Contains("wp-includes", StringComparison.Ordinal)) wpSignals.Add("wp-includes asset");
        if (lowerMarkup.Contains("wp-json", StringComparison.Ordinal)) wpSignals.Add("wp-json metadata");
        if (lowerMarkup.Contains("wp-comments-post.php", StringComparison.Ordinal)) wpSignals.Add("WordPress comment endpoint reference");
        if (form is not null && (postControl is not null ||
                action?.Contains("wp-comments-post.php", StringComparison.OrdinalIgnoreCase) == true ||
                form.Id?.Contains("comment", StringComparison.OrdinalIgnoreCase) == true))
            wpSignals.Add("WordPress-compatible comment form structure");
        if (action?.Contains("wp-comments-post.php", StringComparison.OrdinalIgnoreCase) == true) wpSignals.Add("WordPress comment endpoint");
        if (HeaderContains(response, "wordpress") || HeaderContains(response, "wp-json") ||
            HeaderContains(response, "api.w.org"))
            wpSignals.Add("WordPress response header");
        var wordpress = wpSignals.Count > 0;
        var pageText = Collapse(document.Body?.TextContent ?? string.Empty);
        var loginRequired = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            pageText.Contains("must be logged in to post a comment", StringComparison.OrdinalIgnoreCase);
        var captcha = lowerMarkup.Contains("recaptcha", StringComparison.Ordinal) || lowerMarkup.Contains("hcaptcha", StringComparison.Ordinal) ||
            lowerMarkup.Contains("captcha", StringComparison.Ordinal);
        var commentsClosed = pageText.Contains("comments are closed", StringComparison.OrdinalIgnoreCase) ||
            pageText.Contains("comments closed", StringComparison.OrdinalIgnoreCase) ||
            lowerMarkup.Contains("comments-closed", StringComparison.Ordinal);
        var authorField = FindName(controls, AuthorAliases);
        var emailField = FindName(controls, EmailAliases);
        var websiteField = FindName(controls, WebsiteAliases);
        var commentField = FindName(controls, CommentAliases);
        var resolvedKnownFields = KnownCommentFields.Concat(
                new[] { authorField, emailField, websiteField, commentField, postControl?.GetAttribute("name") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = controls.Where(x => x.HasAttribute("required"))
            .Select(x => x.GetAttribute("name")).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!).Where(x => !resolvedKnownFields.Contains(x))
            .Distinct(StringComparer.Ordinal).Take(32).ToArray();
        var nonce = controls.Any(x => x.GetAttribute("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true &&
            (x.GetAttribute("name")?.Contains("nonce", StringComparison.OrdinalIgnoreCase) == true || x.GetAttribute("name")?.Equals("_wpnonce", StringComparison.OrdinalIgnoreCase) == true));
        var cookieSignal = pageText.Contains("save my name", StringComparison.OrdinalIgnoreCase) || lowerMarkup.Contains("wp-comment-cookies-consent", StringComparison.Ordinal);
        var commentsEnabled = form is not null && !commentsClosed;
        var endpointSameOrigin = endpoint.IsValid && IsSameOrigin(endpoint.NormalizedUrl, final);
        var compatible = wordpress && commentsEnabled && postId is not null && endpointSameOrigin && !loginRequired && !captcha;
        var hardManual = loginRequired || captcha;
        var derivedEndpoint = new Uri(final, "/wp-comments-post.php");
        var fallback = wordpress && effectiveOwnershipAuthorized && !commentsClosed && !hardManual && postId is not null;
        var browserRequired = wordpress && effectiveOwnershipAuthorized && !commentsClosed && !hardManual && !compatible && !fallback;
        var canonicalRaw = document.QuerySelectorAll("link[href]")
            .FirstOrDefault(x => ParseTokens(x.GetAttribute("rel")).Contains("canonical", StringComparer.Ordinal))?.GetAttribute("href");
        var canonical = urlNormalizer.Normalize(canonicalRaw, final);
        var moderationSignal = pageText.Contains("awaiting moderation", StringComparison.OrdinalIgnoreCase)
            ? "Page contains an awaiting-moderation marker."
            : null;

        return new SubmissionSourceValidation(
            wordpress ? SourcePlatform.WordPress : SourcePlatform.GenericWeb,
            wordpress ? CmsType.WordPress : CmsType.Unknown,
            compatible || fallback || browserRequired ? OpportunityType.WordPressComment : OpportunityType.Unknown,
            compatible ? "OwnedWordPressComment" : fallback ? "OwnedWordPressFallbackCommentAdapter" : null,
            compatible ? TechnicalCompatibility.Compatible : fallback ? TechnicalCompatibility.FallbackCandidate :
                browserRequired || hardManual ? TechnicalCompatibility.ManualActionRequired : TechnicalCompatibility.Incompatible,
            SubmissionSourceValidationStatus.Valid,
            compatible ? "WordPress comment submission fields are technically compatible." :
                fallback ? "Authorized WordPress source has page-derived post evidence for bounded fallback submission." :
                browserRequired ? "Authorized WordPress source requires governed browser validation." :
                hardManual ? "Source requires manual action." : commentsClosed ? "WordPress comments are closed." :
                "No compatible WordPress comment flow was detected.",
            wpSignals.Count == 0 ? "No WordPress signals detected." : string.Join("; ", wpSignals),
            captcha || browserRequired,
            loginRequired,
            hardManual || commentsClosed,
            compatible,
            false,
            false,
            postId,
            compatible && endpointSameOrigin ? endpoint.NormalizedUrl : fallback ? derivedEndpoint.AbsoluteUri : null,
            compatible && endpointSameOrigin ? endpoint.NormalizedUrl : null,
            Limit(document.Title, 500),
            final.AbsoluteUri,
            canonical.IsValid ? canonical.NormalizedUrl : null,
            (int)response.StatusCode,
            contentType,
            bytes.LongLength,
            redirects,
            commentsEnabled || fallback,
            authorField,
            emailField,
            websiteField,
            commentField,
            postControl?.GetAttribute("name"),
            required,
            cookieSignal,
            nonce,
            moderationSignal);
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _options.MaximumResponseBytes)
            throw new InvalidDataException("Source response exceeds the configured size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_options.MaximumResponseBytes, 64 * 1_024));
        var buffer = new byte[16 * 1_024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _options.MaximumResponseBytes)
                throw new InvalidDataException("Source response exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static SubmissionSourceValidation Error(string reason, Uri final, IReadOnlyList<string> redirects, int? httpStatus = null, string? contentType = null, long? contentLength = null) =>
        new(SourcePlatform.Unknown, CmsType.Unknown, OpportunityType.Unknown, null, TechnicalCompatibility.Unknown,
            SubmissionSourceValidationStatus.Invalid, reason, reason, false, httpStatus == 401, false,
            false, false, false, null, null, null, null, final.AbsoluteUri, null, httpStatus, contentType, contentLength,
            redirects, false, null, null, null, null, null, [], false, false, null);

    private static SubmissionSourceValidation ResponseTooLarge(Uri final, IReadOnlyList<string> redirects,
        HttpResponseMessage response, bool authorized) => new(SourcePlatform.Unknown, CmsType.Unknown,
        OpportunityType.Unknown, null, TechnicalCompatibility.Unknown, SubmissionSourceValidationStatus.Valid,
        "ResponseTooLargeForStaticInspection", "The bounded static response limit was reached; governed browser validation is required.",
        authorized, false, false, false, false, false, null, null, null, null, final.AbsoluteUri, null,
        (int)response.StatusCode, response.Content.Headers.ContentType?.MediaType,
        response.Content.Headers.ContentLength, redirects, false, null, null, null, null, null, [], false, false, null);

    private static IElement? FindCommentForm(IDocument document)
    {
        var direct = document.QuerySelector("form#commentform") ??
            document.QuerySelector("form[action*='wp-comments-post.php' i]");
        if (direct is not null) return direct;
        foreach (var form in document.QuerySelectorAll("form").Take(MaximumForms))
        {
            var controls = form.QuerySelectorAll("input[name],textarea[name],select[name]")
                .Take(MaximumControls).ToArray();
            var comment = FindControl(controls.Where(x =>
                x.LocalName.Equals("textarea", StringComparison.OrdinalIgnoreCase)), CommentAliases);
            if (comment is null) continue;
            var hasPost = FindControl(controls, PostIdAliases) is not null;
            var hasIdentity = FindControl(controls, AuthorAliases) is not null &&
                FindControl(controls, EmailAliases) is not null;
            if (hasPost || hasIdentity || form.Id?.Contains("comment", StringComparison.OrdinalIgnoreCase) == true) return form;
        }
        return null;
    }

    private static IElement? FindControl(IEnumerable<IElement> controls, params string[] aliases) => controls
        .Take(MaximumControls)
        .Select((control, index) => new { Control = control, Index = index, Score = MatchScore(control, aliases) })
        .Where(value => value.Score > 0)
        .OrderByDescending(value => value.Score)
        .ThenBy(value => value.Index)
        .Select(value => value.Control)
        .FirstOrDefault();

    private static int MatchScore(IElement control, IReadOnlyList<string> aliases)
    {
        var score = Score(control.GetAttribute("name"), aliases, 100, 55);
        score = Math.Max(score, Score(control.Id, aliases, 90, 45));
        score = Math.Max(score, Score(control.GetAttribute("aria-label"), aliases, 80, 40));
        score = Math.Max(score, Score(control.GetAttribute("placeholder"), aliases, 70, 35));
        if (control.ParentElement?.LocalName.Equals("label", StringComparison.OrdinalIgnoreCase) == true)
            score = Math.Max(score, Score(control.ParentElement.TextContent, aliases, 75, 35));
        return score;
    }

    private static int Score(string? value, IReadOnlyList<string> aliases, int exact, int contains)
    {
        var normalized = NormalizeSignal(value);
        if (normalized.Length == 0) return 0;
        var normalizedAliases = aliases.Select(NormalizeSignal);
        if (normalizedAliases.Contains(normalized, StringComparer.Ordinal)) return exact;
        return normalizedAliases.Any(alias => normalized.Contains(alias, StringComparison.Ordinal)) ? contains : 0;
    }

    private static string NormalizeSignal(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Take(100).ToArray());
    private static string? FindName(IEnumerable<IElement> controls, params string[] names) =>
        FindControl(controls, names)?.GetAttribute("name");
    private static long? ParsePostId(string? value) => long.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    private static long? FindRenderedPostId(IDocument document)
    {
        var dataPost = document.QuerySelector("[data-post-id]")?.GetAttribute("data-post-id");
        var parsed = ParsePostId(dataPost);
        if (parsed is not null) return parsed;
        foreach (var element in document.QuerySelectorAll("[id],[class]"))
        {
            var tokens = $"{element.Id} {element.ClassName}".Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var candidate = token.StartsWith("postid-", StringComparison.OrdinalIgnoreCase) ? token[7..]
                    : token.StartsWith("post-", StringComparison.OrdinalIgnoreCase) ? token[5..] : null;
                parsed = ParsePostId(candidate);
                if (parsed is not null) return parsed;
            }
        }
        return null;
    }
    private static bool IsSameOrigin(string? value, Uri expected) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == expected.Scheme && uri.Port == expected.Port &&
        string.Equals(uri.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase);
    private static bool HeaderContains(HttpResponseMessage response, string marker) =>
        response.Headers.Concat(response.Content.Headers).Any(header =>
            header.Value.Any(value => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    private static readonly string[] AuthorAliases = ["author", "name", "display_name", "comment_author"];
    private static readonly string[] EmailAliases = ["email", "mail", "comment_author_email"];
    private static readonly string[] WebsiteAliases = ["url", "website", "site", "homepage", "comment_author_url"];
    private static readonly string[] CommentAliases = ["comment", "message", "body", "content"];
    private static readonly string[] PostIdAliases = ["comment_post_ID", "comment_post_id", "post_id", "postId"];
    private static readonly string[] KnownCommentFields = AuthorAliases.Concat(EmailAliases).Concat(WebsiteAliases)
        .Concat(CommentAliases).Concat(PostIdAliases).Append("comment_parent").ToArray();
    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    private static string[] ParseTokens(string? value) => value?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Limit(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : Collapse(value).Length <= maximum ? Collapse(value) : Collapse(value)[..maximum];
}
