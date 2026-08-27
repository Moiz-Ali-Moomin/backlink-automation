namespace BacklinkStudio.Application;

public sealed record UrlNormalizationResult(bool IsValid, string? NormalizedUrl, string? Domain, string? Error)
{
    public static UrlNormalizationResult Invalid(string error) => new(false, null, null, error);
}

public interface IUrlNormalizer
{
    UrlNormalizationResult Normalize(string? value, Uri? baseUri = null);
}

public sealed class UrlNormalizer : IUrlNormalizer
{
    private const int MaximumUrlLength = 2_048;

    public UrlNormalizationResult Normalize(string? value, Uri? baseUri = null)
    {
        var input = value?.Trim();
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumUrlLength)
        {
            return UrlNormalizationResult.Invalid("URL is required and must not exceed 2048 characters.");
        }

        if (input.Any(character => char.IsControl(character)) || input.Contains('\\') || HasMalformedPercentEncoding(input))
        {
            return UrlNormalizationResult.Invalid("URL contains unsafe or malformed characters.");
        }

        Uri? uri;
        if (input[0] is '/' or '?' or '#')
        {
            if (!IsHttpUri(baseUri) || !Uri.TryCreate(baseUri, input, out uri))
            {
                return UrlNormalizationResult.Invalid("URL must be absolute HTTP(S), or relative to an absolute HTTP(S) base URL.");
            }
        }
        else if (!Uri.TryCreate(input, UriKind.Absolute, out uri))
        {
            if (!IsHttpUri(baseUri) || !Uri.TryCreate(baseUri, input, out uri))
            {
                return UrlNormalizationResult.Invalid("URL must be absolute HTTP(S), or relative to an absolute HTTP(S) base URL.");
            }
        }

        if (!IsHttpUri(uri) || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port == 0)
        {
            return UrlNormalizationResult.Invalid("URL must be an absolute HTTP or HTTPS URL without user information.");
        }

        try
        {
            var domain = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(domain))
            {
                return UrlNormalizationResult.Invalid("URL host is invalid.");
            }

            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = domain,
                Fragment = string.Empty
            };

            if (uri.IsDefaultPort)
            {
                builder.Port = -1;
            }

            var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            builder.Path = string.IsNullOrEmpty(path) ? "/" : path;
            var normalized = builder.Uri.AbsoluteUri;
            if (builder.Path != "/" && string.IsNullOrEmpty(builder.Query))
            {
                normalized = normalized.TrimEnd('/');
            }

            if (normalized.Length > MaximumUrlLength)
            {
                return UrlNormalizationResult.Invalid("Canonical URL must not exceed 2048 characters.");
            }

            return new UrlNormalizationResult(true, normalized, domain, null);
        }
        catch (Exception exception) when (exception is UriFormatException or ArgumentException)
        {
            return UrlNormalizationResult.Invalid("URL could not be canonicalized.");
        }
    }

    private static bool IsHttpUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool HasMalformedPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2]))
            {
                return true;
            }

            index += 2;
        }

        return false;
    }

    private static bool IsHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
