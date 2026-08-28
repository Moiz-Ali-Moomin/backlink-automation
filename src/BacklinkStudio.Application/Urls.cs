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

        // REMOVED: Strict character validation to allow custom payloads and query parameters
        // if (input.Any(character => char.IsControl(character)) || input.Contains('\\') || HasMalformedPercentEncoding(input))
        // {
        //     return UrlNormalizationResult.Invalid("URL contains unsafe or malformed characters.");
        // }

        // Attempt to parse as URI, but allow non-standard formats
        Uri? uri = null;
        bool uriParseSuccess = false;

        try
        {
            if (input[0] is '/' or '?' or '#')
            {
                if (IsHttpUri(baseUri) && Uri.TryCreate(baseUri, input, out uri))
                {
                    uriParseSuccess = true;
                }
            }
            else if (Uri.TryCreate(input, UriKind.Absolute, out uri))
            {
                uriParseSuccess = true;
            }
            else if (IsHttpUri(baseUri) && Uri.TryCreate(baseUri, input, out uri))
            {
                uriParseSuccess = true;
            }

            // REMOVED: Strict URI format validation to allow custom payloads
            // Allow any input as-is if standard URI parsing fails
            if (!uriParseSuccess)
            {
                // Fallback: accept the URL as-is and extract domain from the input string
                // This allows URLs with custom parameters and non-standard formats
                var urlLower = input.ToLowerInvariant();
                if (!urlLower.StartsWith("http://") && !urlLower.StartsWith("https://"))
                {
                    return UrlNormalizationResult.Invalid("URL must start with http:// or https://");
                }

                // Extract domain from raw URL string
                var schemeEnd = input.IndexOf("://", StringComparison.OrdinalIgnoreCase) + 3;
                var pathStart = input.IndexOfAny(new[] { '/', '?', '#' }, schemeEnd);
                var hostPart = pathStart == -1 ? input.Substring(schemeEnd) : input.Substring(schemeEnd, pathStart - schemeEnd);
                var domain = hostPart.Split(':')[0].ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(domain))
                {
                    return UrlNormalizationResult.Invalid("URL host could not be determined.");
                }

                return new UrlNormalizationResult(true, input, domain, null);
            }
        }
        catch (Exception)
        {
            // If URI parsing completely fails, try manual parsing
            var urlLower = input.ToLowerInvariant();
            if (!urlLower.StartsWith("http://") && !urlLower.StartsWith("https://"))
            {
                return UrlNormalizationResult.Invalid("URL must start with http:// or https://");
            }

            var schemeEnd = input.IndexOf("://", StringComparison.OrdinalIgnoreCase) + 3;
            var pathStart = input.IndexOfAny(new[] { '/', '?', '#' }, schemeEnd);
            var hostPart = pathStart == -1 ? input.Substring(schemeEnd) : input.Substring(schemeEnd, pathStart - schemeEnd);
            var domain = hostPart.Split(':')[0].ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(domain))
            {
                return UrlNormalizationResult.Invalid("URL host could not be determined.");
            }

            return new UrlNormalizationResult(true, input, domain, null);
        }

        // REMOVED: Strict URI validation
        // if (!IsHttpUri(uri) || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port == 0)
        // {
        //     return UrlNormalizationResult.Invalid("URL must be an absolute HTTP or HTTPS URL without user information.");
        // }

        try
        {
            if (uri != null)
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
        }
        catch (Exception exception) when (exception is UriFormatException or ArgumentException)
        {
            // Silently continue to fallback
        }

        // Final fallback: return the input as-is if all parsing fails
        return new UrlNormalizationResult(true, input, "unknown", null);
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
