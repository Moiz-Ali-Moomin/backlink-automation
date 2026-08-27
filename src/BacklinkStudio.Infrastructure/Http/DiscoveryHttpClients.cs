using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BacklinkStudio.Application;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class DiscoveryDocumentHttpClient(HttpClient httpClient, IOptions<DiscoveryHttpOptions> options) : IDiscoveryDocumentClient
{
    private readonly DiscoveryHttpOptions _options = options.Value;

    public async Task<DiscoveryDocument> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("Discovery documents require HTTP(S).");
        }

        var current = uri;
        for (var redirect = 0; redirect <= _options.MaximumRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirect == _options.MaximumRedirects || response.Headers.Location is null)
                {
                    throw new InvalidDataException("Discovery document redirect limit exceeded or location missing.");
                }

                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                if (current.Scheme is not ("http" or "https"))
                {
                    throw new InvalidDataException("Discovery document redirected to an unsupported scheme.");
                }
                continue;
            }

            response.EnsureSuccessStatusCode();
            var content = await HttpContentReader.ReadStringAsync(response.Content, _options.MaximumResponseBytes, cancellationToken);
            return new DiscoveryDocument(current.AbsoluteUri, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", content);
        }

        throw new InvalidDataException("Discovery document redirect limit exceeded.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}

public sealed class SerperHttpClient(HttpClient httpClient, IOptions<SerperOptions> options) : ISerperClient
{
    private readonly SerperOptions _options = options.Value;

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int maximumResults, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ValidationException("Serper discovery is not configured.");
        }
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidationException("Serper endpoint must be an absolute HTTPS URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { q = query, num = maximumResults })
        };
        request.Headers.TryAddWithoutValidation("X-API-KEY", _options.ApiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await HttpContentReader.ReadStringAsync(response.Content, 1_048_576, cancellationToken);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("organic", out var organic) || organic.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return organic.EnumerateArray()
            .Select(x => x.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(maximumResults)
            .Select(x => x!)
            .ToArray();
    }
}

internal static class HttpContentReader
{
    public static async Task<string> ReadStringAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Response exceeds the configured discovery size limit.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Response exceeds the configured discovery size limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
