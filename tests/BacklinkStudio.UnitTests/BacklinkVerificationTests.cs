using System.Net;
using System.Text;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class BacklinkVerificationTests
{
    [Fact]
    public void Backlink_TransitionsFromPendingToVerifiedAndThenLost()
    {
        var now = DateTimeOffset.UtcNow;
        var backlink = CreateBacklink(now);

        backlink.ApplyVerification(true, 200, " Acme product ", ["nofollow", "ugc", "sponsored"], "https://source.example/page", null, now.AddMinutes(1));

        Assert.Equal(BacklinkStatus.Verified, backlink.Status);
        Assert.Equal(now.AddMinutes(1), backlink.FirstSeenAt);
        Assert.Equal(now.AddMinutes(1), backlink.LastSeenAt);
        Assert.True(backlink.Nofollow);
        Assert.True(backlink.Ugc);
        Assert.True(backlink.Sponsored);

        backlink.ApplyVerification(false, 404, null, [], null, null, now.AddDays(1));

        Assert.Equal(BacklinkStatus.Lost, backlink.Status);
        Assert.Equal(now.AddMinutes(1), backlink.LastSeenAt);
    }

    [Fact]
    public void Backlink_FirstMissingAndTransportErrorHaveDistinctStates()
    {
        var now = DateTimeOffset.UtcNow;
        var missing = CreateBacklink(now);
        missing.ApplyVerification(false, 200, null, [], null, null, now.AddMinutes(1));
        Assert.Equal(BacklinkStatus.Missing, missing.Status);

        var error = CreateBacklink(now);
        error.ApplyVerification(false, 503, null, [], null, "Transient response", now.AddMinutes(1));
        Assert.Equal(BacklinkStatus.Error, error.Status);
    }

    [Fact]
    public async Task Verifier_RequiresNormalizedAnchorEvidenceAndParsesRelFlags()
    {
        const string html = """
            <html><head><link rel="canonical" href="/source"></head><body>
            <a href="/product#details" rel="NoFoLLoW ugc sponsored"> Acme   product </a>
            </body></html>
            """;
        using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, html, "text/html"));
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(new Uri("https://source.example/source"), "https://source.example/product", TestContext.Current.CancellationToken);

        Assert.True(result.Found);
        Assert.Equal("Acme product", result.Anchor);
        Assert.Equal(["nofollow", "ugc", "sponsored"], result.Rel);
        Assert.Equal("https://source.example/source", result.CanonicalUrl);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task HttpSuccessWithoutMatchingAnchor_IsNotVerified()
    {
        using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK, "<html><body>Submitted successfully</body></html>", "text/html"));
        var result = await CreateVerifier(client).VerifyAsync(new Uri("https://source.example/source"), "https://target.example/product", TestContext.Current.CancellationToken);

        Assert.False(result.Found);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task AuthorizationFailure_IsErrorRatherThanLostEvidence()
    {
        using var client = new HttpClient(new StaticHandler(HttpStatusCode.Forbidden, "denied", "text/plain"));
        var result = await CreateVerifier(client).VerifyAsync(new Uri("https://source.example/source"), "https://target.example/product", TestContext.Current.CancellationToken);

        Assert.False(result.Found);
        Assert.Equal(403, result.HttpStatus);
        Assert.NotNull(result.Error);
    }

    private static Backlink CreateBacklink(DateTimeOffset now) => new(Guid.CreateVersion7(), null, null, "https://source.example/page", "https://source.example/page", "https://target.example/product", "https://target.example/product", "source.example", now);

    private static VerificationHttpClient CreateVerifier(HttpClient client) => new(
        client,
        Options.Create(new VerificationHttpOptions { MaximumResponseBytes = 1_000_000 }),
        new BacklinkStudio.Application.UrlNormalizer(),
        TimeProvider.System);

    private sealed class StaticHandler(HttpStatusCode statusCode, string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, contentType), RequestMessage = request });
    }
}
