using Microsoft.Playwright;

namespace BacklinkStudio.Infrastructure.Http;

internal sealed record ControlledBrowserCommentForm(
    IFrame Frame,
    ILocator Form,
    string? AuthorField,
    string? EmailField,
    string? WebsiteField,
    string CommentField,
    string? PostIdField);

internal static class ControlledBrowserDomInspection
{
    private const int MaximumFrames = 16;
    private const int MaximumForms = 64;
    private const int MaximumControls = 128;
    public const string CommentFormCandidateSelector =
        "form#commentform,form[action*='wp-comments-post.php' i],form:has(textarea[name])";

    private static readonly string[] AuthorAliases = ["author", "name", "display_name", "comment_author"];
    private static readonly string[] EmailAliases = ["email", "mail", "comment_author_email"];
    private static readonly string[] WebsiteAliases = ["url", "website", "site", "homepage", "comment_author_url"];
    private static readonly string[] CommentAliases = ["comment", "message", "body", "content"];
    private static readonly string[] PostIdAliases = ["comment_post_ID", "comment_post_id", "post_id"];

    public static async Task<ControlledBrowserCommentForm?> FindCommentFormAsync(IPage page, string expectedHost)
    {
        ControlledBrowserCommentForm? best = null;
        var bestScore = -1;
        foreach (var frame in SameHostFrames(page, expectedHost))
        {
            var forms = frame.Locator(CommentFormCandidateSelector);
            var count = Math.Min(await forms.CountAsync(), MaximumForms);
            for (var index = 0; index < count; index++)
            {
                var form = forms.Nth(index);
                var comment = await FindFieldNameAsync(form, "textarea[name]", CommentAliases);
                if (comment is null) continue;
                var post = await FindFieldNameAsync(form, "input[name]", PostIdAliases);
                var author = await FindFieldNameAsync(form, "input[name]", AuthorAliases);
                var email = await FindFieldNameAsync(form, "input[name]", EmailAliases);
                var website = await FindFieldNameAsync(form, "input[name]", WebsiteAliases);
                var action = await form.GetAttributeAsync("action");
                var id = await form.GetAttributeAsync("id");
                var wordPressAction = action?.Contains("wp-comments-post.php", StringComparison.OrdinalIgnoreCase) == true;
                var commentFormId = id?.Contains("comment", StringComparison.OrdinalIgnoreCase) == true;
                if (post is null && !wordPressAction && !commentFormId) continue;
                var score = (post is null ? 0 : 8) + (wordPressAction ? 4 : 0) +
                    (commentFormId ? 2 : 0) + (author is null ? 0 : 1) + (email is null ? 0 : 1);
                if (score <= bestScore) continue;
                bestScore = score;
                best = new(frame, form, author, email, website, comment, post);
            }
        }
        return best;
    }

    public static async Task<bool> HasSelectorAsync(IPage page, string expectedHost, string selector)
    {
        foreach (var frame in SameHostFrames(page, expectedHost))
        {
            if (await frame.Locator(selector).CountAsync() > 0) return true;
        }
        return false;
    }

    public static async Task<bool> HasTextAsync(IPage page, string expectedHost, params string[] markers)
    {
        foreach (var frame in SameHostFrames(page, expectedHost))
        {
            foreach (var marker in markers)
            {
                if (await frame.GetByText(marker).CountAsync() > 0) return true;
            }
        }
        return false;
    }

    public static async Task<ILocator?> FindNamedControlAsync(ILocator form, string selector, string? expectedName)
    {
        if (string.IsNullOrWhiteSpace(expectedName)) return null;
        var controls = form.Locator(selector);
        var count = Math.Min(await controls.CountAsync(), MaximumControls);
        for (var index = 0; index < count; index++)
        {
            var control = controls.Nth(index);
            if (string.Equals(await control.GetAttributeAsync("name"), expectedName, StringComparison.Ordinal))
                return control;
        }
        return null;
    }

    private static async Task<string?> FindFieldNameAsync(ILocator form, string selector, string[] aliases)
    {
        string? bestName = null;
        var bestScore = 0;
        var controls = form.Locator(selector);
        var count = Math.Min(await controls.CountAsync(), MaximumControls);
        for (var index = 0; index < count; index++)
        {
            var control = controls.Nth(index);
            var name = await control.GetAttributeAsync("name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var score = Score(name, aliases, 100);
            score = Math.Max(score, Score(await control.GetAttributeAsync("id"), aliases, 90));
            score = Math.Max(score, Score(await control.GetAttributeAsync("aria-label"), aliases, 80));
            score = Math.Max(score, Score(await control.GetAttributeAsync("placeholder"), aliases, 70));
            if (score <= bestScore) continue;
            bestScore = score;
            bestName = name;
        }
        return bestName;
    }

    private static int Score(string? candidate, string[] aliases, int weight)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return 0;
        var normalized = Normalize(candidate);
        foreach (var alias in aliases)
        {
            var expected = Normalize(alias);
            if (normalized == expected) return weight;
            if (normalized.Contains(expected, StringComparison.Ordinal)) return weight / 2;
        }
        return 0;
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static IEnumerable<IFrame> SameHostFrames(IPage page, string expectedHost)
    {
        foreach (var frame in page.Frames.Take(MaximumFrames))
        {
            if (Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri) &&
                string.Equals(uri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase))
                yield return frame;
        }
    }
}
