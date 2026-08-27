using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public static class BlocklistMatcher
{
    public static bool IsBlocked(IEnumerable<BlocklistEntry> entries, string normalizedUrl, string domain) =>
        IsBlocked(entries, new(normalizedUrl, domain, domain, null, null, null));

    public static bool IsBlocked(IEnumerable<BlocklistEntry> entries, BlocklistMatchContext context) => entries.Any(entry =>
        entry.Enabled && entry.MatchType switch
        {
            BlocklistMatchType.Domain => context.Domain.Equals(entry.Value, StringComparison.OrdinalIgnoreCase) ||
                context.Domain.EndsWith('.' + entry.Value, StringComparison.OrdinalIgnoreCase),
            BlocklistMatchType.Host => context.Host.Equals(entry.Value, StringComparison.OrdinalIgnoreCase),
            BlocklistMatchType.Url => context.NormalizedUrl.Equals(entry.Value, StringComparison.OrdinalIgnoreCase),
            BlocklistMatchType.UrlPrefix => context.NormalizedUrl.StartsWith(entry.Value, StringComparison.OrdinalIgnoreCase),
            BlocklistMatchType.SubmissionSource => MatchesId(context.SubmissionSourceId, entry.Value),
            BlocklistMatchType.OwnedNetworkProfile => MatchesId(context.OwnedNetworkProfileId, entry.Value),
            BlocklistMatchType.Campaign => MatchesId(context.CampaignId, entry.Value),
            _ => false
        });

    private static bool MatchesId(Guid? candidate, string value) => candidate is { } id &&
        string.Equals(id.ToString("D"), value, StringComparison.OrdinalIgnoreCase);
}

public sealed record BlocklistMatchContext(string NormalizedUrl, string Domain, string Host,
    Guid? SubmissionSourceId, Guid? OwnedNetworkProfileId, Guid? CampaignId);

public static class DiscoveryProviderNames
{
    public const string ManualUrl = "manual-url";
    public const string TxtImport = "txt-import";
    public const string CsvImport = "csv-import";
    public const string Sitemap = "sitemap";
    public const string Serper = "serper";
    public const string CompetitorBacklinkImport = "competitor-backlink-import";

    public static string For(DiscoveryProviderKind provider) => provider switch
    {
        DiscoveryProviderKind.ManualUrl => ManualUrl,
        DiscoveryProviderKind.TxtImport => TxtImport,
        DiscoveryProviderKind.CsvImport => CsvImport,
        DiscoveryProviderKind.Sitemap => Sitemap,
        DiscoveryProviderKind.Serper => Serper,
        DiscoveryProviderKind.CompetitorBacklinkImport => CompetitorBacklinkImport,
        _ => throw new ValidationException("Unsupported discovery provider.")
    };
}
