using System.Net;
using System.Text;
using BacklinkStudio.Infrastructure.Http;
using BacklinkStudio.Opportunities;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class AnalysisHttpClientTests
{
    [Fact]
    public async Task Analyze_ParsesNormalizedDocumentSignalsWithoutPublicNetworkAccess()
    {
        const string html = """
            <html><head><title>Useful Resources</title><meta name="generator" content="WordPress 6.8"><meta name="robots" content="index,follow"><link rel="canonical" href="/resources/"></head>
            <body><a href="/target#fragment" rel="nofollow ugc">Acme target</a><a href="https://outside.example/path">Outside</a><script src="/wp-content/app.js"></script></body></html>
            """;
        using var httpClient = new HttpClient(new StaticHandler(HttpStatusCode.OK, html, "text/html"));
        var analyzer = Create(httpClient);

        var result = await analyzer.AnalyzeAsync(new Uri("https://source.example/resources"), TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        Assert.Equal("Useful Resources", result.Title);
        Assert.Equal("WordPress", result.Cms);
        Assert.Equal("https://source.example/resources", result.CanonicalUrl);
        Assert.Contains(result.Links, x => x.NormalizedUrl == "https://source.example/target" && x.Rel.Contains("nofollow"));
        Assert.Contains("resource-page", result.EligibleSignals);
        Assert.Equal(1, result.ExternalLinkCount);
    }

    [Fact]
    public async Task Analyze_RejectsOversizedResponse()
    {
        var content = new string('x', 20_000);
        using var httpClient = new HttpClient(new StaticHandler(HttpStatusCode.OK, content, "text/html"));
        var analyzer = Create(httpClient, 16_384);

        var result = await analyzer.AnalyzeAsync(new Uri("https://source.example/"), TestContext.Current.CancellationToken);

        Assert.Equal("Response exceeds the configured analysis size limit.", result.Error);
    }

    private static AnalysisHttpClient Create(HttpClient client, int maximumBytes = 1_000_000) => new(
        client,
        Options.Create(new AnalysisHttpOptions { MaximumResponseBytes = maximumBytes }),
        new BacklinkStudio.Application.UrlNormalizer(),
        [new WordPressDetector(), new GenericDetector()],
        TimeProvider.System);

    private sealed class StaticHandler(HttpStatusCode statusCode, string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, contentType), RequestMessage = request };
            return Task.FromResult(response);
        }
    }
}
