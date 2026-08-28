using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

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