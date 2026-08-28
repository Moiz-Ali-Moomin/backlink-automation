using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Domain;

namespace BacklinkStudio.UnitTests;

public sealed class DiscoveryProviderTests
{
    [Fact]
    public async Task ManualProvider_IsBounded()
    {
        var provider = new ManualUrlProvider();
        var result = await provider.DiscoverAsync(Request(null, null, ["https://one.example", "https://two.example"], 1), TestContext.Current.CancellationToken);
        Assert.Equal(["https://one.example"], result.Urls);
    }

    [Fact]
    public async Task TxtProvider_IgnoresBlankAndCommentLines()
    {
        var provider = new TxtImportProvider();
        var result = await provider.DiscoverAsync(Request(null, "# export\r\nhttps://one.example\n\n https://two.example ", [], 10), TestContext.Current.CancellationToken);
        Assert.Equal(["https://one.example", "https://two.example"], result.Urls);
    }

    [Fact]
    public async Task CsvProvider_ParsesQuotedUrlColumn()
    {
        var provider = new CsvImportProvider();
        var result = await provider.DiscoverAsync(Request(null, "title,url\r\n\"A, useful page\",\"https://one.example/path?a=1,b=2\"", [], 10), TestContext.Current.CancellationToken);
        Assert.Equal("https://one.example/path?a=1,b=2", Assert.Single(result.Urls));
    }

    [Fact]
    public async Task CompetitorProvider_RecognizesReferringPageColumn()
    {
        var provider = new CompetitorBacklinkImportProvider();
        var result = await provider.DiscoverAsync(Request(null, "anchor,referring_page\nAcme,https://source.example/page", [], 10), TestContext.Current.CancellationToken);
        Assert.Equal("https://source.example/page", Assert.Single(result.Urls));
    }

    [Fact]
    public async Task SitemapProvider_FollowsBoundedIndexAndReportsInvalidLocations()
    {
        var documents = new FakeDiscoveryDocuments(new Dictionary<string, string>
        {
            ["https://example.com/index.xml"] = "<sitemapindex xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'><sitemap><loc>https://example.com/pages.xml</loc></sitemap></sitemapindex>",
            ["https://example.com/pages.xml"] = "<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'><url><loc>https://example.com/one</loc></url><url><loc>not-a-url</loc></url><url><loc>https://example.com/two</loc></url></urlset>"
        });
        var provider = new SitemapProvider(documents, new ImmediateRateLimiter());

        var result = await provider.DiscoverAsync(Request("https://example.com/index.xml", null, [], 10), TestContext.Current.CancellationToken);

        Assert.Equal(["https://example.com/one", "https://example.com/two"], result.Urls);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task SitemapProvider_DoesNotFetchCrossHostNestedSitemaps()
    {
        var documents = new FakeDiscoveryDocuments(new Dictionary<string, string>
        {
            ["https://example.com/index.xml"] = "<sitemapindex><sitemap><loc>https://other.example/pages.xml</loc></sitemap></sitemapindex>"
        });
        var provider = new SitemapProvider(documents, new ImmediateRateLimiter());

        var result = await provider.DiscoverAsync(Request("https://example.com/index.xml", null, [], 10), TestContext.Current.CancellationToken);

        Assert.Empty(result.Urls);
        Assert.Contains(result.Errors, x => x.Contains("different host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SerperProvider_DelegatesQueryAndCapsResultsAtOneHundred()
    {
        var client = new FakeSerperClient();
        var provider = new SerperProvider(client, new ImmediateRateLimiter());
        await provider.DiscoverAsync(Request("widgets resources", null, [], 500), TestContext.Current.CancellationToken);
        Assert.Equal(100, client.MaximumResults);
    }

    [Fact]
    public void DiscoveryRun_AllowsCrashResumeAndRecordsHistoryCounters()
    {
        var now = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
        var query = new DiscoveryQuery(Guid.NewGuid(), DiscoveryProviderKind.ManualUrl, null, "{}", 100, now);
        var run = new DiscoveryRun(query.ProjectId, query.Id, now);
        run.AttachJob(Guid.NewGuid());
        run.Start(now.AddSeconds(1));
        run.Start(now.AddSeconds(2));
        run.Complete(5, 2, 1, 1, 1, ["one error"], now.AddSeconds(3));

        Assert.Equal(DiscoveryRunStatus.Succeeded, run.Status);
        Assert.Equal(5, run.UrlsDiscovered);
        Assert.Equal(2, run.UrlsAccepted);
        Assert.Equal(1, run.DuplicateCount);
        Assert.Equal(1, run.BlockedCount);
        Assert.Equal(1, run.InvalidCount);
    }

    private sealed class FakeDiscoveryDocuments(IReadOnlyDictionary<string, string> documents) : IDiscoveryDocumentClient
    {
        public Task<DiscoveryDocument> GetAsync(Uri uri, CancellationToken cancellationToken) => Task.FromResult(
            new DiscoveryDocument(uri.AbsoluteUri, "application/xml", documents[uri.AbsoluteUri]));
    }

    private sealed class FakeSerperClient : ISerperClient
    {
        public int MaximumResults { get; private set; }
        public Task<IReadOnlyList<string>> SearchAsync(string query, int maximumResults, CancellationToken cancellationToken)
        {
            MaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private static DiscoveryRequest Request(string? query, string? content, IReadOnlyList<string> urls, int maximumResults) =>
        new(Guid.NewGuid(), null, query, content, urls, maximumResults);

    private sealed class ImmediateRateLimiter : IDomainRateLimiter
    {
        public Task WaitAsync(Guid projectId, Guid? campaignId, string domain, CancellationToken cancellationToken, int? minimumDelayMilliseconds = null) => Task.CompletedTask;
    }
}