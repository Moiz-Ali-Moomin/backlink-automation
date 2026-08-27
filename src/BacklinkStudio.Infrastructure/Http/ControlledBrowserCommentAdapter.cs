using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Playwright;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class ControlledBrowserCommentAdapter(ControlledBrowserRuntime runtime) : IControlledBrowserCommentAdapter
{
    private const string VisibleSubmitControlSelector =
        "input[type='submit']:visible,button[type='submit']:visible,input#submit:visible,button#submit:visible,input[name='submit']:visible,button[name='submit']:visible";

    public string Name => nameof(ControlledBrowserCommentAdapter);

    public Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request,
        WordPressSiteProfile? profile, CancellationToken cancellationToken)
    {
        if (!request.EffectiveOwnershipAuthorized || !request.Source.Enabled ||
            request.Source.TechnicalCompatibility is not (TechnicalCompatibility.Compatible or TechnicalCompatibility.FallbackCandidate) ||
            request.Source.RequiresAuthentication || request.Source.RequiresManualAction ||
            profile is not null && (!profile.Enabled || profile.SubmissionMode is not
                (WordPressSubmissionMode.ControlledBrowser or WordPressSubmissionMode.StandardComment) ||
                profile.OwnedNetworkProfileId != request.Source.OwnedNetworkProfileId ||
                !string.Equals(profile.Domain, request.Source.Host, StringComparison.Ordinal)))
            return Task.FromResult(Failure(SubmissionFailureKind.PolicyRejected,
                "Controlled browser submission is not authorized for this exact source host."));

        return runtime.UsePageAsync(new(request.Source.FinalUrl ?? request.Source.NormalizedUrl, UriKind.Absolute),
            request.Source.Host, page => SubmitPageAsync(page, request), cancellationToken);
    }

    private static async Task<OwnedWordPressSubmissionResult> SubmitPageAsync(IPage page, OwnedWordPressSubmissionRequest request)
    {
        try
        {
            await page.WaitForSelectorAsync(ControlledBrowserDomInspection.CommentFormCandidateSelector,
                new() { State = WaitForSelectorState.Attached, Timeout = 5_000 });
        }
        catch (TimeoutException) { }
        if (await ControlledBrowserDomInspection.HasSelectorAsync(page, request.Source.Host,
                "iframe[src*='recaptcha' i],iframe[src*='hcaptcha' i],[id*='captcha' i],[class*='captcha' i]"))
            return Failure(SubmissionFailureKind.PolicyRejected, "CAPTCHA requires manual action and is not bypassed.");
        if (await ControlledBrowserDomInspection.HasTextAsync(page, request.Source.Host, "must be logged in"))
            return Failure(SubmissionFailureKind.LoginRequired, "Authentication is required.");
        if (await ControlledBrowserDomInspection.HasTextAsync(page, request.Source.Host,
                "comments are closed", "comments closed"))
            return Failure(SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", SubmissionStatus.Rejected);

        var evidence = await ControlledBrowserDomInspection.FindCommentFormAsync(page, request.Source.Host);
        if (evidence is null) return Failure(SubmissionFailureKind.UnsupportedForm, "The rendered comment form is unavailable.");
        var form = evidence.Form;
        var endpointRaw = await form.GetAttributeAsync("action");
        var pageUri = new Uri(page.Url, UriKind.Absolute);
        var endpoint = ResolveSameOrigin(endpointRaw, pageUri);
        if (endpoint is null) return Failure(SubmissionFailureKind.AuthorizationDenied, "The rendered form endpoint is not same-origin.");

        await FillIfPresentAsync(form, request.Source.CommentAuthorField ?? evidence.AuthorField, request.DisplayName);
        await FillIfPresentAsync(form, request.Source.CommentEmailField ?? evidence.EmailField, request.Email);
        if (!string.IsNullOrWhiteSpace(request.Website))
            await FillIfPresentAsync(form, request.Source.CommentWebsiteField ?? evidence.WebsiteField, request.Website);
        var comment = await ControlledBrowserDomInspection.FindNamedControlAsync(
            form, "textarea[name]", request.Source.CommentContentField ?? evidence.CommentField);
        if (comment is null) return Failure(SubmissionFailureKind.UnsupportedForm, "The rendered comment field is unavailable.");
        await comment.FillAsync(request.Comment);
        var post = await ControlledBrowserDomInspection.FindNamedControlAsync(
            form, "input[name]", request.Source.CommentPostIdField ?? evidence.PostIdField);
        if (post is null || !long.TryParse(await post.GetAttributeAsync("value"), out var postId) || postId != request.Source.PostId)
            return Failure(SubmissionFailureKind.ValidationFailed, "The rendered post identifier does not match validation evidence.");

        IResponse? navigation = null;
        page.Response += (_, response) =>
        {
            if (response.Request.IsNavigationRequest) navigation = response;
        };
        var submit = form.Locator(VisibleSubmitControlSelector).First;
        if (await submit.CountAsync() == 0) return Failure(SubmissionFailureKind.UnsupportedForm, "The rendered submit control is unavailable.");
        await submit.ClickAsync();
        try { await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 10_000 }); }
        catch (TimeoutException) { }
        var final = new Uri(page.Url, UriKind.Absolute);
        if (!string.Equals(final.IdnHost, request.Source.Host, StringComparison.OrdinalIgnoreCase))
            return Failure(SubmissionFailureKind.AuthorizationDenied, "Browser submission redirected to an unauthorized host.", endpoint: endpoint, redirect: final.AbsoluteUri);
        var status = navigation?.Status;
        if (status == 429) return Failure(SubmissionFailureKind.RateLimited, "WordPress rate limited the browser submission.", endpoint: endpoint, redirect: final.AbsoluteUri, httpStatus: status);
        if (status >= 500) return Failure(SubmissionFailureKind.Temporary, "WordPress is temporarily unavailable.", endpoint: endpoint, redirect: final.AbsoluteUri, httpStatus: status);
        if (status == 401) return Failure(SubmissionFailureKind.LoginRequired, "Authentication is required.", SubmissionStatus.ManualActionRequired, endpoint, final.AbsoluteUri, status);
        if (status == 403) return Failure(SubmissionFailureKind.Permanent, "WordPress permanently rejected the browser submission.", SubmissionStatus.Rejected, endpoint, final.AbsoluteUri, status);
        if (await ControlledBrowserDomInspection.HasTextAsync(page, request.Source.Host,
                "comments are closed", "comments closed"))
            return Failure(SubmissionFailureKind.CommentsClosed, "WordPress comments are closed.", SubmissionStatus.Rejected, endpoint, final.AbsoluteUri, status);
        if (await ControlledBrowserDomInspection.HasTextAsync(page, request.Source.Host, "must be logged in"))
            return Failure(SubmissionFailureKind.LoginRequired, "Authentication is required.", SubmissionStatus.ManualActionRequired, endpoint, final.AbsoluteUri, status);
        var pending = await ControlledBrowserDomInspection.HasTextAsync(page, request.Source.Host,
                          "awaiting moderation", "pending moderation") || Contains(final.Query, "unapproved=");
        return new(pending ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted,
            pending ? ModerationStatus.Pending : ModerationStatus.Unknown, WordPressSubmissionMode.ControlledBrowser,
            status, ExtractCommentReference(final.AbsoluteUri), SubmissionFailureKind.None, null, endpoint, final.AbsoluteUri);
    }

    private static async Task FillIfPresentAsync(ILocator form, string? fieldName, string value)
    {
        var field = await ControlledBrowserDomInspection.FindNamedControlAsync(form, "input[name]", fieldName);
        if (field is not null) await field.FillAsync(value);
    }
    private static string? ResolveSameOrigin(string? action, Uri page)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var endpoint = Uri.TryCreate(action, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute
            : new Uri(page, action);
        return endpoint.Scheme == page.Scheme && endpoint.Port == page.Port && string.Equals(endpoint.IdnHost, page.IdnHost, StringComparison.OrdinalIgnoreCase)
            ? endpoint.AbsoluteUri : null;
    }
    private static OwnedWordPressSubmissionResult Failure(SubmissionFailureKind kind, string message,
        SubmissionStatus status = SubmissionStatus.Failed, string? endpoint = null, string? redirect = null, int? httpStatus = null) =>
        new(status, ModerationStatus.Unknown, WordPressSubmissionMode.ControlledBrowser, httpStatus, null, kind, message, endpoint, redirect);
    private static string? ExtractCommentReference(string location)
    {
        var marker = location.LastIndexOf("#comment-", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? null : location[(marker + 9)..].Split('&', '?')[0];
    }
    private static bool Contains(string value, string expected) => value.Contains(expected, StringComparison.OrdinalIgnoreCase);
}
