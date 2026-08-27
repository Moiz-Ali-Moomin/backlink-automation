using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Submission;

namespace BacklinkStudio.UnitTests;

public sealed class OwnedWordPressAdapterSelectionTests
{
    [Fact]
    public async Task CompatiblePublicForm_UsesStandardOnly()
    {
        var standard = new RecordingStandard(Success(WordPressSubmissionMode.StandardComment));
        var fallback = new RecordingFallback(Success(WordPressSubmissionMode.FallbackComment));
        var browser = new RecordingBrowser(Success(WordPressSubmissionMode.ControlledBrowser));
        var adapter = new OwnedWordPressCommentAdapter(standard, fallback, browser, new EmptyProfiles());

        var result = await adapter.SubmitAsync(Request(Source(TechnicalCompatibility.Compatible, false)),
            CancellationToken.None);

        Assert.Equal(WordPressSubmissionMode.StandardComment, result.Strategy);
        Assert.Equal(1, standard.Calls);
        Assert.Equal(0, fallback.Calls);
        Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task AcceptedFallback_StopsBeforeBrowser()
    {
        var standard = new RecordingStandard(Success(WordPressSubmissionMode.StandardComment));
        var fallback = new RecordingFallback(Success(WordPressSubmissionMode.FallbackComment));
        var browser = new RecordingBrowser(Success(WordPressSubmissionMode.ControlledBrowser));
        var adapter = new OwnedWordPressCommentAdapter(standard, fallback, browser, new EmptyProfiles());

        var result = await adapter.SubmitAsync(Request(Source(TechnicalCompatibility.FallbackCandidate, false)),
            CancellationToken.None);

        Assert.Equal(WordPressSubmissionMode.FallbackComment, result.Strategy);
        Assert.Equal(0, standard.Calls);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task CompatibleStandardUnsupported_ContinuesToFallback()
    {
        var standard = new RecordingStandard(new(SubmissionStatus.Failed, ModerationStatus.Unknown,
            WordPressSubmissionMode.StandardComment, null, null, SubmissionFailureKind.UnsupportedForm,
            "form changed"));
        var fallback = new RecordingFallback(Success(WordPressSubmissionMode.FallbackComment));
        var browser = new RecordingBrowser(Success(WordPressSubmissionMode.ControlledBrowser));
        var adapter = new OwnedWordPressCommentAdapter(standard, fallback, browser, new EmptyProfiles());

        var result = await adapter.SubmitAsync(Request(Source(TechnicalCompatibility.Compatible, false)),
            CancellationToken.None);

        Assert.Equal(WordPressSubmissionMode.FallbackComment, result.Strategy);
        Assert.Equal(1, standard.Calls);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task CompatibleStandardAndFallbackUnsupported_ContinuesToCredentiallessBrowser()
    {
        var standard = new RecordingStandard(new(SubmissionStatus.Failed, ModerationStatus.Unknown,
            WordPressSubmissionMode.StandardComment, null, null, SubmissionFailureKind.UnsupportedForm,
            "form changed"));
        var fallback = new RecordingFallback(new(SubmissionStatus.Failed, ModerationStatus.Unknown,
            WordPressSubmissionMode.FallbackComment, null, null, SubmissionFailureKind.EndpointNotFound,
            "endpoint changed"));
        var browser = new RecordingBrowser(Success(WordPressSubmissionMode.ControlledBrowser));
        var adapter = new OwnedWordPressCommentAdapter(standard, fallback, browser, new EmptyProfiles());

        var result = await adapter.SubmitAsync(Request(Source(TechnicalCompatibility.Compatible, false)),
            CancellationToken.None);

        Assert.Equal(WordPressSubmissionMode.ControlledBrowser, result.Strategy);
        Assert.Equal(1, standard.Calls);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal(1, browser.Calls);
        Assert.Null(browser.Profile);
    }

    [Fact]
    public async Task UnsupportedFallback_UsesCredentiallessExactSourceBrowserContext()
    {
        var standard = new RecordingStandard(Success(WordPressSubmissionMode.StandardComment));
        var fallback = new RecordingFallback(new(SubmissionStatus.Failed, ModerationStatus.Unknown,
            WordPressSubmissionMode.FallbackComment, null, null, SubmissionFailureKind.UnsupportedForm,
            "bounded fallback could not resolve the form"));
        var browser = new RecordingBrowser(Success(WordPressSubmissionMode.ControlledBrowser));
        var adapter = new OwnedWordPressCommentAdapter(standard, fallback, browser, new EmptyProfiles());

        var result = await adapter.SubmitAsync(Request(Source(TechnicalCompatibility.FallbackCandidate, true)),
            CancellationToken.None);

        Assert.Equal(WordPressSubmissionMode.ControlledBrowser, result.Strategy);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal(1, browser.Calls);
        Assert.Null(browser.Profile);
    }

    private static SubmissionSource Source(TechnicalCompatibility compatibility, bool requiresBrowser)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new SubmissionSource(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://blog.example/post",
            "https://blog.example/post", "blog.example", "blog.example", OwnershipStatus.Owned, true, null, true,
            now);
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
            nameof(OwnedWordPressCommentAdapter), compatibility, SubmissionSourceValidationStatus.Valid, "eligible",
            "WordPress evidence", requiresBrowser, false, false, compatibility == TechnicalCompatibility.Compatible,
            false, false, 77, "https://blog.example/wp-comments-post.php", null, "Post",
            "https://blog.example/post", null, 200, "text/html", 100, ["https://blog.example/post"], true,
            "author", "email", "url", "comment", "comment_post_ID", [], false, false, null), now);
        return source;
    }

    private static OwnedWordPressSubmissionRequest Request(SubmissionSource source) => new(source, true,
        Guid.CreateVersion7(), Guid.CreateVersion7(), "https://target.example/", "Studio", "studio@example.com",
        "https://target.example/", "A controlled comment", BacklinkPlacementMethod.WebsiteField, "selection-test");

    private static OwnedWordPressSubmissionResult Success(WordPressSubmissionMode strategy) =>
        new(SubmissionStatus.Submitted, ModerationStatus.Unknown, strategy, 200, "1", SubmissionFailureKind.None,
            null);

    private sealed class RecordingStandard(OwnedWordPressSubmissionResult result) : IWordPressSubmissionGateway
    {
        public int Calls { get; private set; }
        public Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingFallback(OwnedWordPressSubmissionResult result) : IOwnedWordPressFallbackCommentAdapter
    {
        public string Name => "fallback";
        public int Calls { get; private set; }
        public Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingBrowser(OwnedWordPressSubmissionResult result) : IControlledBrowserCommentAdapter
    {
        public string Name => "browser";
        public int Calls { get; private set; }
        public WordPressSiteProfile? Profile { get; private set; }
        public Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request,
            WordPressSiteProfile? profile, CancellationToken cancellationToken)
        {
            Calls++;
            Profile = profile;
            return Task.FromResult(result);
        }
    }

    private sealed class EmptyProfiles : IWordPressSiteProfileRepository
    {
        public void Add(WordPressSiteProfile profile) => throw new NotSupportedException();
        public Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
            Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain,
            CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain,
            CancellationToken cancellationToken) => Task.FromResult<WordPressSiteProfile?>(null);
        public Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor,
            int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WordPressSiteProfile>>([]);
    }
}
