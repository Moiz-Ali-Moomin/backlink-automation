using BacklinkStudio.Domain;
using Microsoft.Extensions.Configuration;

namespace BacklinkStudio.Submission;

// The ownership gate these settings used to unlock has been removed, so the override no longer grants
// anything. The settings are still parsed at startup to keep the documented deployment contract: a
// production host that sets BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE=true still fails fast.
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
