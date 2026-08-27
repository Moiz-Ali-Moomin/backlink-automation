using System.Xml;
using System.Xml.Linq;
using BacklinkStudio.Application;

namespace BacklinkStudio.Discovery;

public sealed class ManualUrlProvider : IDiscoveryProvider
{
    public string Name => DiscoveryProviderNames.ManualUrl;

    public Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiscoveryBatchResult(request.Urls.Take(request.MaximumResults).ToArray(), []));
    }
}

public sealed class TxtImportProvider : IDiscoveryProvider
{
    public string Name => DiscoveryProviderNames.TxtImport;

    public Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var urls = TextLines(request.Content)
            .Where(x => !x.StartsWith('#'))
            .Take(request.MaximumResults)
            .ToArray();
        return Task.FromResult(new DiscoveryBatchResult(urls, []));
    }

    internal static IEnumerable<string> TextLines(string? content) => (content ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class CsvImportProvider : IDiscoveryProvider
{
    private static readonly string[] UrlColumns = ["url", "source_url", "sourceurl", "source", "page_url", "pageurl"];
    public string Name => DiscoveryProviderNames.CsvImport;

    public Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CsvUrlParser.Parse(request.Content, request.MaximumResults, UrlColumns));
    }
}

public sealed class CompetitorBacklinkImportProvider : IDiscoveryProvider
{
    private static readonly string[] UrlColumns = ["source_url", "sourceurl", "source", "referring_page", "referringpage", "url"];
    public string Name => DiscoveryProviderNames.CompetitorBacklinkImport;

    public Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CsvUrlParser.Parse(request.Content, request.MaximumResults, UrlColumns));
    }
}

public sealed class SitemapProvider(IDiscoveryDocumentClient documents, IDomainRateLimiter rateLimiter) : IDiscoveryProvider
{
    private const int MaximumSitemapDocuments = 100;
    public string Name => DiscoveryProviderNames.Sitemap;

    public async Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Query, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
        {
            throw new ValidationException("A valid HTTP(S) sitemap URL is required.");
        }

        var pending = new Queue<Uri>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new List<string>();
        var errors = new List<string>();
        pending.Enqueue(root);

        while (pending.Count > 0 && visited.Count < MaximumSitemapDocuments && urls.Count < request.MaximumResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sitemap = pending.Dequeue();
            if (!visited.Add(sitemap.AbsoluteUri))
            {
                continue;
            }

            DiscoveryDocument document;
            try
            {
                await rateLimiter.WaitAsync(request.ProjectId, request.CampaignId, sitemap.IdnHost, cancellationToken);
                document = await documents.GetAsync(sitemap, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or XmlException)
            {
                errors.Add($"Sitemap fetch failed: {SafeMessage(exception.Message)}");
                continue;
            }

            XDocument xml;
            try
            {
                using var reader = XmlReader.Create(new StringReader(document.Content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_097_152 });
                xml = XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException exception)
            {
                errors.Add($"Sitemap XML is invalid: {SafeMessage(exception.Message)}");
                continue;
            }

            var isIndex = xml.Root?.Name.LocalName.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase) == true;
            foreach (var location in xml.Descendants().Where(x => x.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value.Trim()))
            {
                if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                {
                    errors.Add("Sitemap contains an invalid HTTP(S) location.");
                    continue;
                }

                if (isIndex)
                {
                    if (!uri.IdnHost.Equals(root.IdnHost, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add("Sitemap index references a different host; nested fetch was skipped.");
                        continue;
                    }
                    if (pending.Count + visited.Count < MaximumSitemapDocuments)
                    {
                        pending.Enqueue(uri);
                    }
                }
                else
                {
                    urls.Add(uri.AbsoluteUri);
                    if (urls.Count >= request.MaximumResults)
                    {
                        break;
                    }
                }
            }
        }

        if (pending.Count > 0 && visited.Count >= MaximumSitemapDocuments)
        {
            errors.Add($"Sitemap index exceeded the {MaximumSitemapDocuments}-document safety limit.");
        }

        return new DiscoveryBatchResult(urls, errors.Take(100).ToArray());
    }

    private static string SafeMessage(string message) => message.Length <= 300 ? message : message[..300];
}

public sealed class SerperProvider(ISerperClient client, IDomainRateLimiter rateLimiter) : IDiscoveryProvider
{
    public string Name => DiscoveryProviderNames.Serper;

    public async Task<DiscoveryBatchResult> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ValidationException("A Serper search query is required.");
        }

        await rateLimiter.WaitAsync(request.ProjectId, request.CampaignId, "google.serper.dev", cancellationToken);
        var urls = await client.SearchAsync(request.Query, Math.Min(request.MaximumResults, 100), cancellationToken);
        return new DiscoveryBatchResult(urls, []);
    }
}

internal static class CsvUrlParser
{
    public static DiscoveryBatchResult Parse(string? content, int maximumResults, IReadOnlyCollection<string> acceptedHeaders)
    {
        var rows = ParseRows(content ?? string.Empty).ToArray();
        if (rows.Length == 0)
        {
            return new DiscoveryBatchResult([], ["CSV content contains no rows."]);
        }

        var headers = rows[0].Select(NormalizeHeader).ToArray();
        var column = Array.FindIndex(headers, x => acceptedHeaders.Contains(x, StringComparer.Ordinal));
        var firstRowIsHeader = column >= 0;
        if (!firstRowIsHeader)
        {
            column = 0;
        }

        var errors = new List<string>();
        var urls = new List<string>();
        foreach (var row in rows.Skip(firstRowIsHeader ? 1 : 0))
        {
            if (column >= row.Count || string.IsNullOrWhiteSpace(row[column]))
            {
                if (errors.Count < 100)
                {
                    errors.Add("CSV row does not contain a source URL.");
                }
                continue;
            }

            urls.Add(row[column].Trim());
            if (urls.Count >= maximumResults)
            {
                break;
            }
        }

        return new DiscoveryBatchResult(urls, errors);
    }

    private static IEnumerable<IReadOnlyList<string>> ParseRows(string content)
    {
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    yield return row.ToArray();
                }
                row.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        row.Add(field.ToString());
        if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            yield return row.ToArray();
        }
    }

    private static string NormalizeHeader(string value) => value.Trim().TrimStart('\ufeff').Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
