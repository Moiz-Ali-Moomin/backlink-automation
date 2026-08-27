using System.Net;
using System.Net.Sockets;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Http;
using BacklinkStudio.Submission;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.IntegrationTests;

public sealed class ControlledWordPressFlowTests
{
    [Fact]
    public async Task FallbackCommentStrategy_SubmitsAndVerificationRemainsIndependent()
    {
        await using var server = new ControlledWordPressServer(WordPressFixtureMode.Fallback);
        var sourceUrl = $"http://127.0.0.1:{server.Port}/post/owned";
        var endpoint = $"http://127.0.0.1:{server.Port}/wp-comments-post.php";
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), sourceUrl, sourceUrl,
            "127.0.0.1", "127.0.0.1", OwnershipStatus.Controlled, true, null, true, now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            nameof(OwnedWordPressFallbackCommentAdapter), TechnicalCompatibility.FallbackCandidate,
            SubmissionSourceValidationStatus.Valid, "fallback candidate", "WordPress metadata and page-derived post ID",
            false, false, false, false, false, false, 77, endpoint, null, "Owned post", sourceUrl, null, 200,
            "text/html", 256, [sourceUrl], true, "author", "email", "url", "comment", "comment_post_ID", [],
            false, false, null), now);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var adapter = new OwnedWordPressFallbackCommentAdapter(client,
            Options.Create(new WordPressSubmissionOptions { AllowInsecureLoopbackHttpForTesting = true }),
            Options.Create(new WordPressFallbackOptions { MaximumStrategies = 4 }));

        var submission = await adapter.SubmitAsync(new(source, true, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "https://example.com/", "Owned Network", "owned@example.com", "https://example.com/",
                "Controlled fallback comment", BacklinkPlacementMethod.WebsiteField, "fallback-integration-key"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SubmissionStatus.PendingModeration, submission.Status);
        Assert.Equal(ModerationStatus.Pending, submission.ModerationStatus);
        Assert.Equal(WordPressSubmissionMode.FallbackComment, submission.Strategy);
        Assert.Equal(endpoint, submission.Endpoint);
        Assert.Contains("comment_post_ID=77", server.PostBody, StringComparison.Ordinal);
        Assert.Contains("url=https%3A%2F%2Fexample.com%2F", server.PostBody, StringComparison.Ordinal);

        using var verificationClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var verifier = new VerificationHttpClient(verificationClient,
            Options.Create(new VerificationHttpOptions()), new UrlNormalizer(), TimeProvider.System);
        var verification = await verifier.VerifyAsync(new Uri(sourceUrl), "https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.True(verification.Found);
        Assert.Equal(200, verification.HttpStatus);
    }

    [Fact]
    public async Task JavaScriptRenderedCommentFlow_ValidatesSubmitsAndVerifiesIndependently()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_BROWSER_TESTS") != "1",
            "Set BACKLINKSTUDIO_RUN_BROWSER_TESTS=1 on the Linux Playwright test host.");

        await using var server = new JavaScriptWordPressServer();
        var sourceUrl = $"http://127.0.0.1:{server.Port}/post/owned";
        var now = DateTimeOffset.UtcNow;
        var networkId = Guid.CreateVersion7();
        var source = new SubmissionSource(Guid.CreateVersion7(), networkId, sourceUrl, sourceUrl,
            "127.0.0.1", "127.0.0.1", OwnershipStatus.Unverified, false, null, true, now);
        await using var runtime = new ControlledBrowserRuntime(
            Options.Create(new ControlledBrowserOptions
            {
                Enabled = true,
                MaximumConcurrency = 2,
                NavigationTimeoutMilliseconds = 10_000,
                ActionTimeoutMilliseconds = 5_000,
                ChromiumSandbox = false
            }),
            Options.Create(new SubmissionSourceHttpOptions { AllowedPrivateHosts = ["127.0.0.1"] }));
        var validator = new ControlledBrowserValidationAdapter(runtime,
            Options.Create(new ControlledBrowserOptions { ActionTimeoutMilliseconds = 5_000 }));

        var validation = await validator.InspectAsync(source, effectiveOwnershipAuthorized: true,
            TestContext.Current.CancellationToken);
        source.ApplyValidation(validation, now.AddSeconds(1));

        Assert.True(validation.TechnicalCompatibility == TechnicalCompatibility.Compatible,
            $"compatibility={validation.TechnicalCompatibility};reason={validation.ValidationReason};detection={validation.DetectionReason};postId={validation.PostId};endpoint={validation.CommentEndpoint};fields={string.Join(',', validation.AdditionalRequiredFields)}");
        Assert.True(source.RequiresBrowser);
        Assert.Equal(nameof(ControlledBrowserCommentAdapter), source.AdapterName);
        Assert.Equal(77, source.PostId);
        Assert.Equal("visitor", source.CommentAuthorField);
        Assert.Equal("contact", source.CommentEmailField);
        Assert.Equal("homepage", source.CommentWebsiteField);
        Assert.Equal("reply_text", source.CommentContentField);
        Assert.Equal("post_id", source.CommentPostIdField);
        Assert.Equal(OwnershipStatus.Unverified, source.OwnershipStatus);
        Assert.False(source.AutomationPermitted);

        var submitter = new ControlledBrowserCommentAdapter(runtime);
        var submission = await submitter.SubmitAsync(new(source, true, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "https://example.com/", "Owned Network", "owned@example.com", "https://example.com/",
                "Controlled browser comment for https://example.com/", BacklinkPlacementMethod.WebsiteField,
                "browser-integration-key"),
            null, TestContext.Current.CancellationToken);

        Assert.Equal(SubmissionStatus.PendingModeration, submission.Status);
        Assert.Equal(ModerationStatus.Pending, submission.ModerationStatus);
        Assert.Equal(WordPressSubmissionMode.ControlledBrowser, submission.Strategy);
        Assert.Contains("post_id=77", server.PostBody, StringComparison.Ordinal);
        Assert.Contains("homepage=https%3A%2F%2Fexample.com%2F", server.PostBody, StringComparison.Ordinal);
        Assert.Equal(OwnershipStatus.Unverified, source.OwnershipStatus);
        Assert.False(source.AutomationPermitted);

        using var verificationClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var verifier = new VerificationHttpClient(verificationClient,
            Options.Create(new VerificationHttpOptions()), new UrlNormalizer(), TimeProvider.System);
        var verification = await verifier.VerifyAsync(new Uri(sourceUrl), "https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.True(verification.Found);
        Assert.Equal(200, verification.HttpStatus);
        Assert.Null(verification.Error);
    }

    [Fact]
    public async Task StandardCommentStrategy_SubmitsToRealControlledHttpServer()
    {
        await using var server = new ControlledWordPressServer();
        var sourceUrl = $"http://127.0.0.1:{server.Port}/post/owned";
        var endpoint = $"http://127.0.0.1:{server.Port}/wp-comments-post.php";
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), sourceUrl, sourceUrl,
            "127.0.0.1", "127.0.0.1", OwnershipStatus.Owned, true, null, true, now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            nameof(OwnedWordPressCommentAdapter), TechnicalCompatibility.Compatible, SubmissionSourceValidationStatus.Valid,
            "compatible", "controlled WordPress comment form", false, false, false, true, false, false, 77,
            endpoint, endpoint, "Owned post", sourceUrl, null, 200, "text/html", 512, [sourceUrl], true,
            "author", "email", "url", "comment", "comment_post_ID", ["_wpnonce"], true, true, null), now);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var gateway = new WordPressSubmissionGateway(client, new EmptyProfiles(), Options.Create(new WordPressSubmissionOptions
        {
            AllowInsecureLoopbackHttpForTesting = true
        }));

        var result = await gateway.SubmitAsync(new(source, true, Guid.CreateVersion7(), Guid.CreateVersion7(),
            "https://target.example/", "Owned Network", "owned@example.com", "https://target.example/",
            "Controlled integration comment", BacklinkPlacementMethod.WebsiteField, "integration-key"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SubmissionStatus.Submitted, result.Status);
        Assert.Equal(ModerationStatus.Approved, result.ModerationStatus);
        Assert.Equal("701", result.ExternalReference);
        Assert.Contains("comment_post_ID=77", server.PostBody, StringComparison.Ordinal);
        Assert.Contains("_wpnonce=controlled-nonce", server.PostBody, StringComparison.Ordinal);
        Assert.Contains("url=https%3A%2F%2Ftarget.example%2F", server.PostBody, StringComparison.Ordinal);
        Assert.Equal("wordpress_owned=1", server.Cookie);
    }

    [Theory]
    [InlineData(WordPressFixtureMode.Moderated, SubmissionStatus.PendingModeration, SubmissionFailureKind.None, ModerationStatus.Pending)]
    [InlineData(WordPressFixtureMode.CommentsClosed, SubmissionStatus.Failed, SubmissionFailureKind.CommentsClosed, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.Duplicate, SubmissionStatus.Duplicate, SubmissionFailureKind.Duplicate, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.InvalidEmail, SubmissionStatus.Failed, SubmissionFailureKind.Permanent, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.InvalidPost, SubmissionStatus.Rejected, SubmissionFailureKind.InvalidPost, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.RateLimited, SubmissionStatus.Failed, SubmissionFailureKind.RateLimited, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.LoginRequired, SubmissionStatus.ManualActionRequired, SubmissionFailureKind.LoginRequired, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.Forbidden, SubmissionStatus.Rejected, SubmissionFailureKind.Permanent, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.TemporaryFailure, SubmissionStatus.Failed, SubmissionFailureKind.Temporary, ModerationStatus.Unknown)]
    [InlineData(WordPressFixtureMode.PermanentFailure, SubmissionStatus.Failed, SubmissionFailureKind.Permanent, ModerationStatus.Unknown)]
    public async Task StandardCommentStrategy_ClassifiesControlledNetworkOutcomes(WordPressFixtureMode mode,
        SubmissionStatus expectedStatus, SubmissionFailureKind expectedFailure, ModerationStatus expectedModeration)
    {
        await using var server = new ControlledWordPressServer(mode);
        var sourceUrl = $"http://127.0.0.1:{server.Port}/post/owned";
        var endpoint = $"http://127.0.0.1:{server.Port}/wp-comments-post.php";
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), sourceUrl, sourceUrl,
            "127.0.0.1", "127.0.0.1", OwnershipStatus.Controlled, true, null, true, now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            nameof(OwnedWordPressCommentAdapter), TechnicalCompatibility.Compatible, SubmissionSourceValidationStatus.Valid,
            "compatible", "controlled fixture", false, false, false, true, false, false, 77, endpoint, endpoint,
            "Owned post", sourceUrl, null, 200, "text/html", 512, [sourceUrl], true, "author", "email", "url",
            "comment", "comment_post_ID", ["_wpnonce"], true, true, null), now);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var gateway = new WordPressSubmissionGateway(client, new EmptyProfiles(), Options.Create(new WordPressSubmissionOptions
        {
            AllowInsecureLoopbackHttpForTesting = true
        }));

        var result = await gateway.SubmitAsync(new(source, true, Guid.CreateVersion7(), Guid.CreateVersion7(),
            "https://target.example/", "Owned Network", "owned@example.com", "https://target.example/",
            "Controlled integration comment", BacklinkPlacementMethod.WebsiteField, $"fixture-{mode}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedFailure, result.FailureKind);
        Assert.Equal(expectedModeration, result.ModerationStatus);
    }

    private sealed class EmptyProfiles : IWordPressSiteProfileRepository
    {
        public void Add(WordPressSiteProfile profile) => throw new NotSupportedException();
        public Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressSiteProfile>>([]);
    }

    private sealed class ControlledWordPressServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;

        private readonly WordPressFixtureMode _mode;

        public ControlledWordPressServer(WordPressFixtureMode mode = WordPressFixtureMode.Published)
        {
            _mode = mode;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _server = ServeAsync(_stop.Token);
        }

        public int Port { get; }
        public string PostBody { get; private set; } = string.Empty;
        public string? Cookie { get; private set; }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            var expectedRequests = _mode switch
            {
                WordPressFixtureMode.CommentsClosed => 1,
                WordPressFixtureMode.Fallback => 3,
                _ => 2
            };
            for (var requestNumber = 0; requestNumber < expectedRequests; requestNumber++)
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, cancellationToken);
                if (requestNumber == 0)
                {
                    var form = _mode == WordPressFixtureMode.CommentsClosed ? "<main>Comments are closed.</main>" :
                        _mode == WordPressFixtureMode.Fallback
                            ? "<html><head><meta name='generator' content='WordPress'></head><body class='postid-77'><div class='wp-content'>Owned post</div></body></html>" :
                        $"<form id='commentform' action='http://127.0.0.1:{Port}/wp-comments-post.php'>" +
                        "<input type='hidden' name='_wpnonce' value='controlled-nonce'>" +
                        "<input type='hidden' name='comment_post_ID' value='77'><textarea name='comment'></textarea></form>";
                    await WriteResponseAsync(stream, "200 OK", form,
                        "Content-Type: text/html; charset=utf-8\r\nSet-Cookie: wordpress_owned=1; Path=/; HttpOnly\r\n", cancellationToken);
                }
                else if (_mode == WordPressFixtureMode.Fallback && requestNumber == 2)
                {
                    await WriteResponseAsync(stream, "200 OK",
                        "<html><body><a href='https://example.com/'>Verified controlled fallback</a></body></html>",
                        "Content-Type: text/html; charset=utf-8\r\n", cancellationToken);
                }
                else
                {
                    PostBody = request.Body;
                    Cookie = request.Headers.TryGetValue("Cookie", out var cookie) ? cookie : null;
                    var response = ResponseForMode();
                    await WriteResponseAsync(stream, response.Status, response.Body, response.Headers, cancellationToken);
                }
            }
        }

        private FixtureResponse ResponseForMode() => _mode switch
        {
            WordPressFixtureMode.Published => new("302 Found", string.Empty, $"Location: http://127.0.0.1:{Port}/post/owned#comment-701\r\n"),
            WordPressFixtureMode.Fallback => new("302 Found", string.Empty, $"Location: http://127.0.0.1:{Port}/post/owned?unapproved=1#comment-703\r\n"),
            WordPressFixtureMode.Moderated => new("302 Found", string.Empty, $"Location: http://127.0.0.1:{Port}/post/owned?unapproved=1#comment-702\r\n"),
            WordPressFixtureMode.Duplicate => new("409 Conflict", "Duplicate comment detected.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.InvalidEmail => new("400 Bad Request", "Please enter a valid email address.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.InvalidPost => new("400 Bad Request", "Invalid post identifier.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.RateLimited => new("429 Too Many Requests", "Slow down.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.LoginRequired => new("403 Forbidden", "You must be logged in to post a comment.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.Forbidden => new("403 Forbidden", "Comment permanently rejected.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.TemporaryFailure => new("503 Service Unavailable", "Temporarily unavailable.", "Content-Type: text/plain\r\n"),
            WordPressFixtureMode.PermanentFailure => new("400 Bad Request", "Comment rejected.", "Content-Type: text/plain\r\n"),
            _ => throw new InvalidOperationException("Unsupported fixture mode.")
        };

        private static async Task<HttpRequestData> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>(4_096);
            var buffer = new byte[1_024];
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
                headerEnd = FindHeaderEnd(bytes);
            }
            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd + 4).ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines.Skip(1).Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var contentLength = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            var bodyBytes = bytes.Skip(headerEnd + 4).ToList();
            while (bodyBytes.Count < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, contentLength - bodyBytes.Count)), cancellationToken);
                if (read == 0) break;
                bodyBytes.AddRange(buffer.AsSpan(0, read).ToArray());
            }
            return new(headers, Encoding.UTF8.GetString(bodyBytes.Take(contentLength).ToArray()));
        }

        private static int FindHeaderEnd(List<byte> value)
        {
            for (var index = 3; index < value.Count; index++)
                if (value[index - 3] == 13 && value[index - 2] == 10 && value[index - 1] == 13 && value[index] == 10)
                    return index - 3;
            return -1;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string status, string body, string headers,
            CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{headers}Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _server; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }

        private sealed record HttpRequestData(IReadOnlyDictionary<string, string> Headers, string Body);
        private sealed record FixtureResponse(string Status, string Body, string Headers);
    }

    private sealed class JavaScriptWordPressServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        private volatile bool _submitted;

        public JavaScriptWordPressServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _server = ServeAsync(_stop.Token);
        }

        public int Port { get; }
        public string PostBody { get; private set; } = string.Empty;

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                var request = await ReadBrowserRequestAsync(stream, cancellationToken);
                if (request.Method == "POST" && request.Path.StartsWith("/wp-comments-post.php", StringComparison.Ordinal))
                {
                    PostBody = request.Body;
                    _submitted = true;
                    await WriteBrowserResponseAsync(stream, "200 OK",
                        "<html><body>Your comment is awaiting moderation.</body></html>", cancellationToken);
                    continue;
                }

                var backlink = _submitted
                    ? "<article><a href='https://example.com/'>Controlled browser backlink</a></article>"
                    : string.Empty;
                var page = $$"""
                    <!doctype html><html><head><meta name="generator" content="WordPress 6.8"></head><body>
                    {{backlink}}<div id="comment-root"></div>
                    <script>
                    setTimeout(() => {
                      document.getElementById('comment-root').innerHTML =
                        `<form id="commentform" method="post" action="/wp-comments-post.php">
                         <input name="visitor" aria-label="Author name" required>
                         <input name="contact" aria-label="Email address" required>
                         <input name="homepage" aria-label="Website">
                         <textarea name="reply_text" aria-label="Comment" required></textarea>
                         <input type="hidden" name="post_id" value="77">
                           <button type="submit">Post Comment</button></form>`;
                    }, 50);
                    </script></body></html>
                    """;
                await WriteBrowserResponseAsync(stream, "200 OK", page, cancellationToken);
            }
        }

        private static async Task<BrowserRequest> ReadBrowserRequestAsync(NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var bytes = new List<byte>(4_096);
            var buffer = new byte[1_024];
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
                headerEnd = FindHeaderEnd(bytes);
            }
            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd + 4).ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var requestLine = lines[0].Split(' ', 3);
            var headers = lines.Skip(1).Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var contentLength = headers.TryGetValue("Content-Length", out var value)
                ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            var bodyBytes = bytes.Skip(headerEnd + 4).ToList();
            while (bodyBytes.Count < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, contentLength - bodyBytes.Count)), cancellationToken);
                if (read == 0) break;
                bodyBytes.AddRange(buffer.AsSpan(0, read).ToArray());
            }
            return new(requestLine[0], requestLine[1], Encoding.UTF8.GetString(bodyBytes.Take(contentLength).ToArray()));
        }

        private static async Task WriteBrowserResponseAsync(NetworkStream stream, string status, string body,
            CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        private static int FindHeaderEnd(List<byte> value)
        {
            for (var index = 3; index < value.Count; index++)
                if (value[index - 3] == 13 && value[index - 2] == 10 && value[index - 1] == 13 && value[index] == 10)
                    return index - 3;
            return -1;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _server; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }

        private sealed record BrowserRequest(string Method, string Path, string Body);
    }

    public enum WordPressFixtureMode
    {
        Published,
        Fallback,
        Moderated,
        CommentsClosed,
        Duplicate,
        InvalidEmail,
        InvalidPost,
        RateLimited,
        LoginRequired,
        Forbidden,
        TemporaryFailure,
        PermanentFailure
    }
}
