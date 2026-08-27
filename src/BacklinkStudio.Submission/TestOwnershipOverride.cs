using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Configuration;

namespace BacklinkStudio.Submission;

public sealed record TestOwnershipOverrideOptions(
    bool Enabled,
    IReadOnlySet<string> AllowedHosts,
    string EnvironmentName)
{
    private const int MaximumAllowedHosts = 100;
    public const string SectionName = "TestOwnershipOverride";
    public const string EnabledEnvironmentVariable = "BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE";
    public const string AllowedHostsEnvironmentVariable = "BACKLINKSTUDIO_TEST_ALLOWED_HOSTS";

    public static TestOwnershipOverrideOptions FromConfiguration(
        IConfiguration configuration,
        string environmentName)
    {
        var enabledValue = configuration[EnabledEnvironmentVariable]
            ?? configuration[$"{SectionName}:Enabled"];
        var enabled = false;
        if (!string.IsNullOrWhiteSpace(enabledValue) && !bool.TryParse(enabledValue, out enabled))
            throw new InvalidOperationException($"{EnabledEnvironmentVariable} must be 'true' or 'false'.");

        var configuredHosts = configuration[AllowedHostsEnvironmentVariable];
        var hostValues = configuredHosts is null
            ? configuration.GetSection($"{SectionName}:AllowedHosts").GetChildren().Select(x => x.Value)
            : configuredHosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var allowedHosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hostValue in hostValues)
        {
            if (string.IsNullOrWhiteSpace(hostValue)) continue;
            try
            {
                allowedHosts.Add(OwnedNetworkDomain.NormalizeHost(hostValue));
            }
            catch (DomainRuleException exception)
            {
                throw new InvalidOperationException(
                    $"{AllowedHostsEnvironmentVariable} contains an invalid exact DNS host.", exception);
            }

            if (allowedHosts.Count > MaximumAllowedHosts)
                throw new InvalidOperationException(
                    $"{AllowedHostsEnvironmentVariable} cannot contain more than {MaximumAllowedHosts} exact hosts.");
        }

        if (enabled && !IsPermittedEnvironment(environmentName))
            throw new InvalidOperationException(
                $"{EnabledEnvironmentVariable}=true is permitted only in Development or Test; current environment is '{environmentName}'.");

        return new(enabled, allowedHosts, environmentName);
    }

    private static bool IsPermittedEnvironment(string environmentName) =>
        environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
        environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase);
}

public sealed class TestOwnershipExecutionAuthorizerDecorator(
    IOwnedNetworkExecutionAuthorizer inner,
    TestOwnershipOverrideOptions options) : IOwnedNetworkExecutionAuthorizer
{
    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(
        Guid projectId,
        Guid ownedNetworkProfileId,
        CancellationToken cancellationToken)
    {
        var decision = await inner.AuthorizeProfileAsync(projectId, ownedNetworkProfileId, cancellationToken);
        return CanOverride(decision, requireExactHost: false)
            ? AllowedByTestOverride(decision.Profile!)
            : decision;
    }

    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        OwnedNetworkSourceAuthorizationRequest source,
        CancellationToken cancellationToken)
    {
        var decision = await inner.AuthorizeSourceAsync(projectId, source, cancellationToken);
        var normalizedHost = OwnedNetworkDomain.NormalizeHost(source.Host);
        return CanOverride(decision, requireExactHost: true) && options.AllowedHosts.Contains(normalizedHost)
            ? AllowedByTestOverride(decision.Profile!)
            : decision;
    }

    private bool CanOverride(OwnedNetworkExecutionAuthorization decision, bool requireExactHost) =>
        !decision.Allowed &&
        options.Enabled &&
        decision.Reason == OwnedNetworkExecutionAuthorizer.OwnershipNotAuthorizedReason &&
        decision.Profile is { Enabled: true } &&
        (!requireExactHost || options.AllowedHosts.Count > 0);

    private static OwnedNetworkExecutionAuthorization AllowedByTestOverride(OwnedNetworkProfile profile) =>
        new(true, true, "test_ownership_override", profile);
}
