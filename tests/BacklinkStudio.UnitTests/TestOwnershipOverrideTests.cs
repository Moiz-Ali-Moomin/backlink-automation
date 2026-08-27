using BacklinkStudio.Submission;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.UnitTests;

public sealed class TestOwnershipOverrideTests
{
    [Fact]
    public void Production_Enabled_FailsDuringServiceConfiguration()
    {
        var configuration = Configuration(true, "wordpress-open");
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBacklinkStudioSubmission(configuration, "Production"));

        Assert.Contains("permitted only in Development or Test", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestEnvironment_Enabled_IsAccepted()
    {
        var options = Options("Test", true, "wordpress-open");

        Assert.True(options.Enabled);
        Assert.Contains("wordpress-open", options.AllowedHosts);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("10.0.0.0/8")]
    [InlineData("wordpress-open:8080")]
    public void InvalidNonExactAllowlistEntries_FailConfiguration(string host)
    {
        Assert.Throws<InvalidOperationException>(() => Options("Test", true, host));
    }

    private static TestOwnershipOverrideOptions Options(string environment, bool enabled, string hosts) =>
        TestOwnershipOverrideOptions.FromConfiguration(Configuration(enabled, hosts), environment);

    private static IConfiguration Configuration(bool enabled, string hosts) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [TestOwnershipOverrideOptions.EnabledEnvironmentVariable] = enabled.ToString(),
            [TestOwnershipOverrideOptions.AllowedHostsEnvironmentVariable] = hosts
        }).Build();
}
