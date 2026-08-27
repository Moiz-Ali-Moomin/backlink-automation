namespace BacklinkStudio.Application;

public interface IDiscoveryProvider
{
    string Name { get; }

    Task<DiscoveryBatchResult> DiscoverAsync(
        DiscoveryRequest request,
        CancellationToken cancellationToken);
}

public sealed record DiscoveryRequest(
    Guid ProjectId,
    Guid? CampaignId,
    string? Query,
    string? Content,
    IReadOnlyList<string> Urls,
    int MaximumResults);

public sealed record DiscoveryBatchResult(
    IReadOnlyList<string> Urls,
    IReadOnlyList<string> Errors);

public sealed record DiscoveryDocument(string FinalUrl, string ContentType, string Content);

public interface IDiscoveryDocumentClient
{
    Task<DiscoveryDocument> GetAsync(Uri uri, CancellationToken cancellationToken);
}

public interface ISerperClient
{
    Task<IReadOnlyList<string>> SearchAsync(string query, int maximumResults, CancellationToken cancellationToken);
}
