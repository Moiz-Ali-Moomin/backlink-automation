using System.Net;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class WordPressSubmissionTests
{
    [Fact]
    public void SiteProfile_AllowsOwnedHttpMetadataWhileGatewayRetainsTransportEnforcement()
    {
        var networkId = Guid.CreateVersion7();

        var profile = new WordPressSiteProfile(networkId, "wordpress-direct", "http://wordpress-direct/",
            "disposable-direct-api", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow);

        Assert.Equal("http://wordpress-direct/", profile.ApiBaseUrl);
        Assert.Throws<DomainRuleException>(() => new WordPressSiteProfile(networkId, "wordpress-direct",
            "ftp://wordpress-direct/", "disposable-direct-api", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SiteProfile_CredentialIsOptionalOnlyForPublicCommentModes()
    {
        var networkId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var standard = new WordPressSiteProfile(networkId, "blog.example", "https://blog.example/", null,
            WordPressSubmissionMode.StandardComment, true, now);
        var browser = new WordPressSiteProfile(networkId, "blog.example", "https://blog.example/", null,
            WordPressSubmissionMode.ControlledBrowser, true, now);

        Assert.Null(standard.CredentialReference);
        Assert.Null(browser.CredentialReference);
        Assert.Throws<DomainRuleException>(() => new WordPressSiteProfile(networkId, "blog.example",
            "https://blog.example/", null, WordPressSubmissionMode.DirectApi, true, now));
        Assert.Throws<DomainRuleException>(() => new WordPressSiteProfile(networkId, "blog.example",
            "https://blog.example/", null, WordPressSubmissionMode.AuthenticatedIntegration, true, now));
    }

    [Fact]
    public async Task StandardStrategy_ReplaysHiddenFieldsAndClassifiesModeration()
    {
        var source = CompatibleSource();
        var handler = new StandardWordPressHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new EmptyProfiles(),
            Options.Create(new WordPressSubmissionOptions()));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.PendingModeration, result.Status);
        Assert.Equal(ModerationStatus.Pending, result.ModerationStatus);
        Assert.Equal(WordPressSubmissionMode.StandardComment, result.Strategy);
        Assert.Equal("42", result.ExternalReference);
        Assert.Contains("_wpnonce=nonce-123", handler.PostBody, StringComparison.Ordinal);
        Assert.Contains("comment_post_ID=77", handler.PostBody, StringComparison.Ordinal);
        Assert.Contains("comment=Resolved+comment", handler.PostBody, StringComparison.Ordinal);
        Assert.Equal("wordpress_test=1", handler.Cookie);
    }

    [Fact]
    public async Task StandardStrategy_RefreshesValidatedFinalUrlAfterCanonicalRedirect()
    {
        var source = CompatibleSource("https://blog.example/post/");
        var handler = new StandardWordPressHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new EmptyProfiles(),
            Options.Create(new WordPressSubmissionOptions()));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.PendingModeration, result.Status);
        Assert.Equal("https://blog.example/post/", handler.GetUri?.AbsoluteUri);
    }

    [Fact]
    public async Task DirectApi_UsesNamedCredentialAndDoesNotPlaceSecretInPayload()
    {
        var source = CompatibleSource();
        var profile = new WordPressSiteProfile(source.OwnedNetworkProfileId!.Value, source.Host, "https://blog.example/",
            "owned-blog", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow);
        var handler = new DirectWordPressHandler();
        var settings = new WordPressSubmissionOptions();
        settings.Credentials["owned-blog"] = new() { Username = "operator", ApplicationPassword = "app-secret" };
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new SingleProfile(profile), Options.Create(settings));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.Submitted, result.Status);
        Assert.Equal(WordPressSubmissionMode.DirectApi, result.Strategy);
        Assert.Equal("901", result.ExternalReference);
        Assert.Equal("https://blog.example/wp-json/wp/v2/comments", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.DoesNotContain("app-secret", handler.PostBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectApi_RejectsRevokedOrMissingNamedCredentialBeforeNetworkIo()
    {
        var source = CompatibleSource();
        var profile = new WordPressSiteProfile(source.OwnedNetworkProfileId!.Value, source.Host, "https://blog.example/",
            "revoked-owned-blog", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow);
        var handler = new DirectWordPressHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new SingleProfile(profile),
            Options.Create(new WordPressSubmissionOptions()));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.Failed, result.Status);
        Assert.Equal(SubmissionFailureKind.AuthorizationDenied, result.FailureKind);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain("revoked-owned-blog", result.SafeError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectApi_ClassifiesTemporaryFailureForRetryWithoutLeakingCredential()
    {
        var source = CompatibleSource();
        var profile = new WordPressSiteProfile(source.OwnedNetworkProfileId!.Value, source.Host, "https://blog.example/",
            "owned-blog", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow);
        var handler = new DirectWordPressHandler(HttpStatusCode.ServiceUnavailable,
            "{\"code\":\"rest_unavailable\"}");
        var settings = new WordPressSubmissionOptions();
        settings.Credentials["owned-blog"] = new() { Username = "operator", ApplicationPassword = "app-secret" };
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new SingleProfile(profile), Options.Create(settings));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.Failed, result.Status);
        Assert.Equal(SubmissionFailureKind.Temporary, result.FailureKind);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, result.HttpStatus);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("app-secret", handler.PostBody, StringComparison.Ordinal);
        Assert.DoesNotContain("app-secret", result.SafeError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectApi_ClassifiesPermanentAuthenticationRejectionWithoutLeakingCredential()
    {
        var source = CompatibleSource();
        var profile = new WordPressSiteProfile(source.OwnedNetworkProfileId!.Value, source.Host, "https://blog.example/",
            "owned-blog", WordPressSubmissionMode.DirectApi, true, DateTimeOffset.UtcNow);
        var handler = new DirectWordPressHandler(HttpStatusCode.Unauthorized,
            "{\"code\":\"rest_cannot_create\",\"message\":\"invalid application password\"}");
        var settings = new WordPressSubmissionOptions();
        settings.Credentials["owned-blog"] = new() { Username = "operator", ApplicationPassword = "app-secret" };
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new SingleProfile(profile), Options.Create(settings));

        var result = await gateway.SubmitAsync(Request(source), CancellationToken.None);

        Assert.Equal(SubmissionStatus.Failed, result.Status);
        Assert.Equal(SubmissionFailureKind.LoginRequired, result.FailureKind);
        Assert.Equal((int)HttpStatusCode.Unauthorized, result.HttpStatus);
        Assert.DoesNotContain("app-secret", result.SafeError, StringComparison.Ordinal);
        Assert.DoesNotContain("app-secret", handler.PostBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserRequiredOwnedSource_StopsAsManualActionBeforeNetworkIo()
    {
        var handler = new CountingHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new EmptyProfiles(),
            Options.Create(new WordPressSubmissionOptions()));

        var result = await gateway.SubmitAsync(Request(CompatibleSource(requiresBrowser: true)), CancellationToken.None);

        Assert.Equal(SubmissionStatus.ManualActionRequired, result.Status);
        Assert.Equal(WordPressSubmissionMode.ManualActionRequired, result.Strategy);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DisabledOwnedSource_IsRejectedBeforeNetworkIo()
    {
        var handler = new CountingHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new EmptyProfiles(),
            Options.Create(new WordPressSubmissionOptions()));

        var result = await gateway.SubmitAsync(Request(CompatibleSource(enabled: false)), CancellationToken.None);

        Assert.Equal(SubmissionStatus.ManualActionRequired, result.Status);
        Assert.Equal(SubmissionFailureKind.PolicyRejected, result.FailureKind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ValidatedRedirectOutsideOwnedHost_IsRejectedBeforeNetworkIo()
    {
        var handler = new CountingHandler();
        var gateway = new WordPressSubmissionGateway(new HttpClient(handler), new EmptyProfiles(),
            Options.Create(new WordPressSubmissionOptions()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.SubmitAsync(
            Request(CompatibleSource("https://outside.example/post/")), CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    private static SubmissionSource CompatibleSource(string? finalUrl = null, bool requiresBrowser = false, bool enabled = true)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://blog.example/post",
            "https://blog.example/post", "blog.example", "blog.example", OwnershipStatus.Owned, true, null, enabled, now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            "OwnedWordPressCommentAdapter", TechnicalCompatibility.Compatible, SubmissionSourceValidationStatus.Valid,
            "compatible", "WordPress form", requiresBrowser, false, false, true, false, false, 77,
            "https://blog.example/wp-comments-post.php", "https://blog.example/wp-comments-post.php", "Post",
            finalUrl ?? "https://blog.example/post", null, 200, "text/html", 100, ["https://blog.example/post"], true,
            "author", "email", "url", "comment", "comment_post_ID", ["_wpnonce"], true, true, null), now);
        return source;
    }

    private static OwnedWordPressSubmissionRequest Request(SubmissionSource source) => new(source, true, Guid.CreateVersion7(),
        Guid.CreateVersion7(), "https://target.example/", "Studio", "studio@example.com", "https://target.example/",
        "Resolved comment", BacklinkPlacementMethod.WebsiteField, "stable-key");

    private sealed class StandardWordPressHandler : HttpMessageHandler
    {
        public string? PostBody { get; private set; }
        public string? Cookie { get; private set; }
        public Uri? GetUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                GetUri = request.RequestUri;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<form id='commentform' action='https://blog.example/wp-comments-post.php'><input type='hidden' name='_wpnonce' value='nonce-123'><input type='hidden' name='comment_post_ID' value='77'><textarea name='comment'></textarea></form>", Encoding.UTF8, "text/html")
                };
                response.Headers.TryAddWithoutValidation("Set-Cookie", "wordpress_test=1; Path=/; Secure; HttpOnly");
                return response;
            }
            PostBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            Cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
            var submitted = new HttpResponseMessage(HttpStatusCode.Found);
            submitted.Headers.Location = new("https://blog.example/post?unapproved=1#comment-42");
            return submitted;
        }
    }

    private sealed class DirectWordPressHandler(
        HttpStatusCode statusCode = HttpStatusCode.Created,
        string responseBody = "{\"id\":901,\"status\":\"approved\"}") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? PostBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            PostBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class EmptyProfiles : IWordPressSiteProfileRepository
    {
        public void Add(WordPressSiteProfile profile) => throw new NotSupportedException();
        public Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressSiteProfile>>([]);
    }

    private sealed class SingleProfile(WordPressSiteProfile profile) : IWordPressSiteProfileRepository
    {
        public void Add(WordPressSiteProfile value) => throw new NotSupportedException();
        public Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(id == profile.Id ? profile : null);
        public Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(ownedNetworkProfileId == profile.OwnedNetworkProfileId && domain == profile.Domain ? profile : null);
        public Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) => FindForSourceAsync(ownedNetworkProfileId, domain, cancellationToken);
        public Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressSiteProfile>>([profile]);
    }
}
