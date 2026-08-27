using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public static class DiscoveryCommandValidation
{
    public static void Validate(StartDiscoveryCommand command)
    {
        if (command.MaximumResults is < 1 or > 5_000)
        {
            throw new ValidationException("maximumResults must be between 1 and 5000.");
        }
        if (command.Urls?.Count > 5_000 || command.Urls?.Any(x => x is null || x.Length > 2_048) == true)
        {
            throw new ValidationException("Discovery accepts at most 5000 URL values of up to 2048 characters each.");
        }
        if (command.Content?.Length > 524_288)
        {
            throw new ValidationException("Discovery import content must not exceed 512 KiB.");
        }

        switch (command.Provider)
        {
            case DiscoveryProviderKind.ManualUrl when command.Urls is not { Count: > 0 }:
                throw new ValidationException("Manual URL discovery requires at least one URL.");
            case DiscoveryProviderKind.TxtImport or DiscoveryProviderKind.CsvImport or DiscoveryProviderKind.CompetitorBacklinkImport when string.IsNullOrWhiteSpace(command.Content):
                throw new ValidationException("This import provider requires content.");
            case DiscoveryProviderKind.Sitemap when !IsHttpUrl(command.Query):
                throw new ValidationException("Sitemap discovery requires a valid HTTP(S) query URL.");
            case DiscoveryProviderKind.Serper when string.IsNullOrWhiteSpace(command.Query) || command.Query.Length > 500:
                throw new ValidationException("Serper discovery requires a query of at most 500 characters.");
            case DiscoveryProviderKind.Serper when command.MaximumResults > 100:
                throw new ValidationException("Serper discovery supports at most 100 results per run.");
        }
    }

    private static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
