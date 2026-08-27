using System.Net;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class SubmissionSourceValidationTests
{
    [Fact]
    public async Task Inspector_ExplainsWordPressAndCommentCapability()
    {
        const string html = """
            <html><head><title>Owned Post</title><meta name="generator" content="WordPress 6.9">
            <link rel="https://api.w.org/" href="/wp-json/"><link rel="canonical" href="/post/owned"></head>
            <body><form id="commentform" action="/wp-comments-post.php" method="post">
            <input name="author" required><input name="email" required><input name="url">
            <textarea name="comment" required></textarea>
            <input type="hidden" name="comment_post_ID" value="42">
            <input type="hidden" name="_wpnonce" value="redacted">
            <input name="network_code" required></form></body></html>
            """;
        var inspector = CreateInspector(HttpStatusCode.OK, html);

        var result = await inspector.InspectAsync(new Uri("https://owned.example/post/owned"), CancellationToken.None);

        Assert.Equal(CmsType.WordPress, result.CmsType);
        Assert.Equal(OpportunityType.WordPressComment, result.OpportunityType);
        Assert.Equal(TechnicalCompatibility.Compatible, result.TechnicalCompatibility);
        Assert.Equal("OwnedWordPressComment", result.AdapterName);
        Assert.Equal(42, result.PostId);
        Assert.Equal("https://owned.example/wp-comments-post.php", result.CommentEndpoint);
        Assert.Equal("author", result.CommentAuthorField);
        Assert.Equal("email", result.CommentEmailField);
        Assert.Equal("url", result.CommentWebsiteField);
        Assert.Equal("comment", result.CommentContentField);
        Assert.Equal("comment_post_ID", result.CommentPostIdField);
        Assert.Contains("network_code", result.AdditionalRequiredFields);
        Assert.True(result.RequiresNonce);
        Assert.Contains("generator metadata", result.DetectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspector_ClassifiesCaptchaAsManualActionWithoutBypass()
    {
        const string html = """
            <html><body><div class="g-recaptcha"></div><form id="commentform" action="/wp-comments-post.php">
            <textarea name="comment"></textarea><input name="comment_post_ID" value="7"></form></body></html>
            """;
        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://owned.example/post"), CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.ManualActionRequired, result.TechnicalCompatibility);
        Assert.True(result.RequiresBrowser);
        Assert.True(result.RequiresManualAction);
        Assert.False(result.SupportsWordPressComment);
    }

    [Fact]
    public async Task Inspector_MatchesBoundedSemanticPublicFormAliases()
    {
        const string html = """
            <html><head><meta name="generator" content="WordPress"></head><body>
            <form class="public-reply" action="/comments/receive">
              <label>Display name<input name="visitor" aria-label="display_name" required></label>
              <label>Email address<input name="contact" aria-label="comment_author_email" required></label>
              <label>Homepage<input name="profile_link" aria-label="homepage"></label>
              <label>Message<textarea name="reply_text" aria-label="body" required></textarea></label>
              <input type="hidden" name="post_id" value="314"><input type="hidden" name="comment_parent" value="0">
              <button type="submit">Post</button>
            </form></body></html>
            """;

        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://owned.example/post"), true, CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.Compatible, result.TechnicalCompatibility);
        Assert.Equal("visitor", result.CommentAuthorField);
        Assert.Equal("contact", result.CommentEmailField);
        Assert.Equal("profile_link", result.CommentWebsiteField);
        Assert.Equal("reply_text", result.CommentContentField);
        Assert.Equal("post_id", result.CommentPostIdField);
        Assert.Equal(314, result.PostId);
        Assert.Equal("https://owned.example/comments/receive", result.CommentEndpoint);
    }

    [Fact]
    public async Task AuthorizedWordPressWithoutRecognizedForm_BecomesFallbackCandidateFromPagePostId()
    {
        const string html = "<html><head><meta name='generator' content='WordPress 6.9'></head><body class='postid-91'><div class='wp-content'>Post</div></body></html>";

        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://owned.example/post"), true, CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.FallbackCandidate, result.TechnicalCompatibility);
        Assert.Equal("OwnedWordPressFallbackCommentAdapter", result.AdapterName);
        Assert.Equal(91, result.PostId);
        Assert.Equal("https://owned.example/wp-comments-post.php", result.CommentEndpoint);
    }

    [Fact]
    public async Task UnauthorizedWordPressWithoutRecognizedForm_DoesNotBecomeFallbackCandidate()
    {
        const string html = "<html><head><meta name='generator' content='WordPress'></head><body class='postid-91'></body></html>";

        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://unowned.example/post"), false, CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.Incompatible, result.TechnicalCompatibility);
        Assert.Null(result.AdapterName);
    }

    [Fact]
    public async Task CrossOriginFormAction_IsNeverPersistedAsFallbackEndpoint()
    {
        const string html = "<html><head><meta name='generator' content='WordPress'></head><body><form action='https://evil.example/post'><textarea name='comment'></textarea><input name='comment_post_ID' value='91'></form></body></html>";

        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://owned.example/post"), true, CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.FallbackCandidate, result.TechnicalCompatibility);
        Assert.Equal("https://owned.example/wp-comments-post.php", result.CommentEndpoint);
    }

    [Fact]
    public async Task CommentsClosed_RemainsBlockedForAuthorizedWordPressSource()
    {
        const string html = "<html><head><meta name='generator' content='WordPress'></head><body class='postid-91 comments-closed'>Comments are closed.</body></html>";

        var result = await CreateInspector(HttpStatusCode.OK, html)
            .InspectAsync(new Uri("https://owned.example/post"), true, CancellationToken.None);

        Assert.Equal(TechnicalCompatibility.Incompatible, result.TechnicalCompatibility);
        Assert.True(result.RequiresManualAction);
    }

    [Fact]
    public async Task OversizedStaticResponse_BecomesAuthorizedBrowserCandidateInsteadOfInvalid()
    {
        var handler = new StaticHandler(HttpStatusCode.OK, new string('x', 2_048));
        var inspector = new SubmissionSourceHttpInspector(new HttpClient(handler),
            Options.Create(new SubmissionSourceHttpOptions { MaximumResponseBytes = 256 }), new UrlNormalizer());

        var result = await inspector.InspectAsync(new Uri("https://owned.example/post"), true,
            CancellationToken.None);

        Assert.Equal(SubmissionSourceValidationStatus.Valid, result.ValidationStatus);
        Assert.Equal("ResponseTooLargeForStaticInspection", result.ValidationReason);
        Assert.True(result.RequiresBrowser);
        Assert.False(result.RequiresManualAction);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Inspector_DistinguishesAuthenticationFromPermanentForbidden(HttpStatusCode status,
        bool authenticationRequired)
    {
        var result = await CreateInspector(status, "rejected")
            .InspectAsync(new Uri("https://owned.example/post"), true, CancellationToken.None);

        Assert.Equal(SubmissionSourceValidationStatus.Invalid, result.ValidationStatus);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Equal(authenticationRequired, result.RequiresAuthentication);
    }

    private static SubmissionSourceHttpInspector CreateInspector(HttpStatusCode status, string body)
    {
        var handler = new StaticHandler(status, body);
        return new SubmissionSourceHttpInspector(new HttpClient(handler), Options.Create(new SubmissionSourceHttpOptions()), new UrlNormalizer());
    }

    private sealed class StaticHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
    }
}
