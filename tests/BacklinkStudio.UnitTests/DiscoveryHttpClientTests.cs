using System.Net;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class DiscoveryHttpClientTests
{
    [Fact]
    public async Task DocumentClient_RejectsOversizedResponse()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[101])
        }));
        var client = new DiscoveryDocumentHttpClient(httpClient, Options.Create(new DiscoveryHttpOptions { MaximumResponseBytes = 100 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(new Uri("https://example.com/sitemap.xml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DocumentClient_RejectsRedirectToUnsupportedScheme()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("file:///etc/passwd") }
        }));
        var client = new DiscoveryDocumentHttpClient(httpClient, Options.Create(new DiscoveryHttpOptions()));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(new Uri("https://example.com/sitemap.xml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SerperClient_SendsConfiguredSecretAndParsesOnlyOrganicLinks()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            observed = request;
            body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"organic\":[{\"link\":\"https://one.example\"},{\"title\":\"missing\"},{\"link\":\"https://two.example\"}]}", Encoding.UTF8, "application/json")
            };
        }));
        var client = new SerperHttpClient(httpClient, Options.Create(new SerperOptions { ApiKey = "test-secret" }));

        var result = await client.SearchAsync("useful widgets", 1, TestContext.Current.CancellationToken);

        Assert.Equal("test-secret", observed!.Headers.GetValues("X-API-KEY").Single());
        Assert.Contains("useful widgets", body, StringComparison.Ordinal);
        Assert.Equal(["https://one.example"], result);
    }

    [Fact]
    public async Task SerperClient_FailsSafelyWhenNotConfigured()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new SerperHttpClient(httpClient, Options.Create(new SerperOptions()));
        await Assert.ThrowsAsync<ValidationException>(() => client.SearchAsync("query", 10, TestContext.Current.CancellationToken));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : this(request => Task.FromResult(response(request))) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
