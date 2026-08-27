using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Submission;

public sealed class SubmissionOptions
{
    public const string SectionName = "Submission";
    public bool AllowInsecureLoopbackHttpForTesting { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, SubmissionProfileOptions> Profiles { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SubmissionProfileOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string SourceDomain { get; set; } = string.Empty;
    public OpportunityType[] AllowedTypes { get; set; } = [];
    public string? BearerToken { get; set; }
}

public sealed class SubmissionOptionsValidator : IValidateOptions<SubmissionOptions>
{
    public ValidateOptionsResult Validate(string? name, SubmissionOptions options)
    {
        if (options.TimeoutSeconds is < 1 or > 120) return ValidateOptionsResult.Fail("Submission timeout must be between 1 and 120 seconds.");
        if (options.Profiles.Count > 100) return ValidateOptionsResult.Fail("At most 100 submission authorization profiles may be configured.");
        foreach (var (key, profile) in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint))
                return ValidateOptionsResult.Fail("Every submission profile requires a valid key and absolute endpoint.");
            var secure = endpoint.Scheme == Uri.UriSchemeHttps;
            var allowedTestHttp = options.AllowInsecureLoopbackHttpForTesting && endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback;
            if (!secure && !allowedTestHttp)
                return ValidateOptionsResult.Fail($"Submission profile '{key}' must use HTTPS; HTTP is allowed only for loopback tests.");
            if (!string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
                return ValidateOptionsResult.Fail($"Submission profile '{key}' endpoint must not contain user information, a query, or a fragment.");
            var sourceDomain = profile.SourceDomain.Trim().TrimEnd('.');
            if (sourceDomain.Length is < 1 or > 253 || !string.Equals(endpoint.IdnHost, sourceDomain, StringComparison.OrdinalIgnoreCase))
                return ValidateOptionsResult.Fail($"Submission profile '{key}' endpoint host must equal its source domain.");
            if (profile.AllowedTypes.Length is < 1 or > 20 || profile.AllowedTypes.Distinct().Count() != profile.AllowedTypes.Length || profile.AllowedTypes.Contains(OpportunityType.Unknown) || profile.AllowedTypes.Contains(OpportunityType.ManualOutreach))
                return ValidateOptionsResult.Fail($"Submission profile '{key}' requires at least one automatable opportunity type.");
            if (profile.BearerToken?.Length > 8_192)
                return ValidateOptionsResult.Fail($"Submission profile '{key}' bearer token is too long.");
        }
        return ValidateOptionsResult.Success;
    }
}

public sealed class SubmissionAuthorizationResolver(IOptions<SubmissionOptions> options) : ISubmissionAuthorizationResolver
{
    public SubmissionAuthorization Resolve(string profileKey)
    {
        if (!options.Value.Profiles.TryGetValue(profileKey, out var profile))
            throw new UnauthorizedAccessException("The named submission authorization profile is not configured.");
        return new SubmissionAuthorization(profileKey, profile.SourceDomain.Trim().TrimEnd('.').ToLowerInvariant(), profile.AllowedTypes.ToHashSet());
    }
}
