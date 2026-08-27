using System.Net;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class WordPressFallbackTests
{
    [Fact]
    public async Task DerivedEndpoint_UsesPagePostId_AndRedirectIsNotVerification()
    {
        var handler = new FallbackHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal(SubmissionStatus.PendingModeration, result.Status);
        Assert.Equal(WordPressSubmissionMode.FallbackComment, result.Strategy);
        Assert.Equal("https://blog.example/wp-comments-post.php", result.Endpoint);
        Assert.Equal("https://blog.example/post?unapproved=1#comment-42", result.RedirectDestination);
        Assert.Equal(1, handler.PostCalls);
    }

    [Fact]
    public async Task MaximumStrategyCount_IsEnforced()
    {
        var handler = new FallbackHandler("<html><head><meta name='generator' content='WordPress'></head><body class='postid-77'><form action='/missing'><textarea name='message'></textarea></form></body></html>", HttpStatusCode.NotFound);
        var adapter = CreateAdapter(handler, maximumStrategies: 1);

        var result = await adapter.SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal(SubmissionFailureKind.EndpointNotFound, result.FailureKind);
        Assert.Equal(1, handler.PostCalls);
    }

    [Fact]
    public async Task CrossOriginDiscoveredAction_IsIgnoredInFavorOfSameOriginEndpoint()
    {
        var html = "<html><head><meta name='generator' content='WordPress'></head><body class='postid-77'><form action='https://evil.example/comments'><textarea name='message'></textarea></form></body></html>";
        var handler = new FallbackHandler(html);

        var result = await CreateAdapter(handler).SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal("blog.example", handler.PostUri?.Host);
        Assert.Equal(SubmissionFailureKind.None, result.FailureKind);
    }

    [Fact]
    public async Task AuthenticationRequired_IsPermanentAndDoesNotPost()
    {
        var handler = new FallbackHandler("<html><head><meta name='generator' content='WordPress'></head><body class='postid-77'>You must be logged in to post a comment.</body></html>");

        var result = await CreateAdapter(handler).SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal(SubmissionFailureKind.LoginRequired, result.FailureKind);
        Assert.Equal(0, handler.PostCalls);
    }

    [Fact]
    public async Task RateLimit_IsClassifiedTransiently()
    {
        var handler = new FallbackHandler(postStatus: HttpStatusCode.TooManyRequests);

        var result = await CreateAdapter(handler).SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal(SubmissionFailureKind.RateLimited, result.FailureKind);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, result.HttpStatus);
        Assert.Equal(TimeSpan.FromSeconds(17), result.RetryAfter);
    }

    [Fact]
    public async Task OversizedPage_ContinuesToBrowserWithoutPostingFromFallback()
    {
        var handler = new FallbackHandler($"<meta name='generator' content='WordPress'><div>{new string('x', 512)}</div>");

        var result = await CreateAdapter(handler, maximumResponseBytes: 128)
            .SubmitAsync(Request(FallbackSource()), CancellationToken.None);

        Assert.Equal(SubmissionFailureKind.BrowserRequired, result.FailureKind);
        Assert.Equal(0, handler.PostCalls);
    }

    [Fact]
    public async Task ControlledBrowserAdapter_RejectsUnauthorizedSourceBeforeBrowserLaunch()
    {
        var runtime = new ControlledBrowserRuntime(Options.Create(new ControlledBrowserOptions()),
            Options.Create(new SubmissionSourceHttpOptions()));
        await using (runtime)
        {
            var adapter = new ControlledBrowserValidationAdapter(runtime, Options.Create(new ControlledBrowserOptions()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                adapter.InspectAsync(FallbackSource(), false, CancellationToken.None));
        }
    }

    private static OwnedWordPressFallbackCommentAdapter CreateAdapter(FallbackHandler handler, int maximumStrategies = 4,
        int? maximumResponseBytes = null)
    {
        var submission = new WordPressSubmissionOptions();
        if (maximumResponseBytes is not null) submission.MaximumResponseBytes = maximumResponseBytes.Value;
        return new(new HttpClient(handler), Options.Create(submission),
            Options.Create(new WordPressFallbackOptions { MaximumStrategies = maximumStrategies }));
    }

    private static SubmissionSource FallbackSource()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://blog.example/post",
            "https://blog.example/post", "blog.example", "blog.example", OwnershipStatus.Owned, true, null, true, now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            "OwnedWordPressFallbackCommentAdapter", TechnicalCompatibility.FallbackCandidate,
            SubmissionSourceValidationStatus.Valid, "fallback", "WordPress evidence", false, false, false, false,
            false, false, 77, "https://blog.example/wp-comments-post.php", null, "Post",
            "https://blog.example/post", null, 200, "text/html", 100, ["https://blog.example/post"], true,
            "author", "email", "url", "comment", "comment_post_ID", [], false, false, null), now);
        return source;
    }

    private static OwnedWordPressSubmissionRequest Request(SubmissionSource source) => new(source, true,
        Guid.CreateVersion7(), Guid.CreateVersion7(), "https://target.example/", "Studio", "studio@example.com",
        "https://target.example/", "Resolved comment", BacklinkPlacementMethod.WebsiteField, "stable-key");

    private sealed class FallbackHandler(
        string pageHtml = "<html><head><meta name='generator' content='WordPress'></head><body class='postid-77'><div class='wp-content'>Post</div></body></html>",
        HttpStatusCode postStatus = HttpStatusCode.Found) : HttpMessageHandler
    {
        public int PostCalls { get; private set; }
        public Uri? PostUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
                });
            PostCalls++;
            PostUri = request.RequestUri;
            var response = new HttpResponseMessage(postStatus)
            {
                Content = new StringContent(postStatus == HttpStatusCode.TooManyRequests ? "rate limited" : "awaiting moderation", Encoding.UTF8, "text/html")
            };
            if (postStatus == HttpStatusCode.Found)
                response.Headers.Location = new Uri("https://blog.example/post?unapproved=1#comment-42");
            if (postStatus == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        }
    }
}
