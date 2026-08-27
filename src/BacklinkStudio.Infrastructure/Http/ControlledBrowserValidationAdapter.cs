using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class ControlledBrowserValidationAdapter(
    ControlledBrowserRuntime runtime,
    IOptions<ControlledBrowserOptions> options) : IControlledBrowserValidationAdapter
{
    private readonly ControlledBrowserOptions _options = options.Value;

    public Task<SubmissionSourceValidation> InspectAsync(SubmissionSource source, bool effectiveOwnershipAuthorized,
        CancellationToken cancellationToken)
    {
        if (!effectiveOwnershipAuthorized || !source.Enabled)
            throw new UnauthorizedAccessException("Controlled browser validation requires server-side source authorization.");
        return runtime.UsePageAsync(new(source.NormalizedUrl, UriKind.Absolute), source.Host,
            page => InspectPageAsync(page, source), cancellationToken);
    }

    private async Task<SubmissionSourceValidation> InspectPageAsync(IPage page, SubmissionSource source)
    {
        try
        {
            await page.WaitForSelectorAsync(ControlledBrowserDomInspection.CommentFormCandidateSelector,
                new() { State = WaitForSelectorState.Attached, Timeout = Math.Min(_options.ActionTimeoutMilliseconds, 5_000) });
        }
        catch (TimeoutException) { }
        var evidence = await ControlledBrowserDomInspection.FindCommentFormAsync(page, source.Host);
        var wordpress = evidence?.PostIdField is not null || await ControlledBrowserDomInspection.HasSelectorAsync(page, source.Host,
            "meta[name='generator'][content*='wordpress' i],link[href*='wp-content' i],script[src*='wp-content' i]," +
            "link[href*='wp-includes' i],script[src*='wp-includes' i],link[href*='wp-json' i]," +
            "form[action*='wp-comments-post.php' i]");
        var captcha = await ControlledBrowserDomInspection.HasSelectorAsync(page, source.Host,
            "iframe[src*='recaptcha' i],iframe[src*='hcaptcha' i],[id*='captcha' i],[class*='captcha' i]");
        var login = await ControlledBrowserDomInspection.HasTextAsync(page, source.Host, "must be logged in");
        var closed = await ControlledBrowserDomInspection.HasTextAsync(page, source.Host,
            "comments are closed", "comments closed");
        var form = evidence?.Form;
        var hasForm = form is not null;
        var post = form is null ? null : await ControlledBrowserDomInspection.FindNamedControlAsync(
            form, "input[name]", evidence!.PostIdField);
        var postId = post is not null && long.TryParse(await post.GetAttributeAsync("value"), out var parsed) && parsed > 0
            ? parsed : source.PostId;
        var action = hasForm ? await form!.GetAttributeAsync("action") : null;
        var final = new Uri(page.Url, UriKind.Absolute);
        var endpoint = ResolveSameOrigin(action, final);
        var compatible = wordpress && hasForm && post is not null && postId is not null && endpoint is not null &&
            !captcha && !login && !closed;
        var required = hasForm ? await RequiredUnknownFieldsAsync(form!, evidence!) : Array.Empty<string>();

        return new(
            wordpress ? SourcePlatform.WordPress : SourcePlatform.GenericWeb,
            wordpress ? CmsType.WordPress : CmsType.Unknown,
            compatible ? OpportunityType.WordPressComment : OpportunityType.Unknown,
            compatible ? nameof(ControlledBrowserCommentAdapter) : null,
            compatible ? TechnicalCompatibility.Compatible : captcha || login || closed ? TechnicalCompatibility.ManualActionRequired : TechnicalCompatibility.Incompatible,
            SubmissionSourceValidationStatus.Valid,
            compatible ? "Governed browser validation resolved a JavaScript-rendered WordPress comment form." :
                captcha ? "CAPTCHA requires manual action and is not bypassed." : login ? "Authentication is required." :
                closed ? "WordPress comments are closed." : "Rendered DOM does not contain a supported comment form.",
            wordpress ? "WordPress evidence found in the rendered DOM." : "No WordPress evidence found in the rendered DOM.",
            compatible, login, captcha || login || closed, compatible, false, false, postId, endpoint, endpoint,
            await page.TitleAsync(), final.AbsoluteUri, null, null, "text/html", null,
            new[] { source.NormalizedUrl, final.AbsoluteUri }.Distinct(StringComparer.Ordinal).ToArray(), compatible,
            evidence?.AuthorField,
            evidence?.EmailField,
            evidence?.WebsiteField,
            evidence?.CommentField,
            evidence?.PostIdField,
            required, false, hasForm && await form!.Locator("input[name*='nonce' i]").CountAsync() > 0, null);
    }

    private static async Task<string[]> RequiredUnknownFieldsAsync(ILocator form, ControlledBrowserCommentForm evidence)
    {
        var known = new HashSet<string>(new[]
        {
            evidence.AuthorField, evidence.EmailField, evidence.WebsiteField, evidence.CommentField, evidence.PostIdField
        }.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        var controls = form.Locator("input[name][required],textarea[name][required],select[name][required]");
        for (var index = 0; index < Math.Min(await controls.CountAsync(), 32); index++)
        {
            var name = await controls.Nth(index).GetAttributeAsync("name");
            if (!string.IsNullOrWhiteSpace(name) && !known.Contains(name)) values.Add(name);
        }
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ResolveSameOrigin(string? action, Uri page)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var endpoint = Uri.TryCreate(action, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute
            : new Uri(page, action);
        return endpoint.Scheme == page.Scheme && endpoint.Port == page.Port &&
            string.Equals(endpoint.IdnHost, page.IdnHost, StringComparison.OrdinalIgnoreCase) ? endpoint.AbsoluteUri : null;
    }
}
