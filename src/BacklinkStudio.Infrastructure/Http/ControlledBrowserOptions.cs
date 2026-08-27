namespace BacklinkStudio.Infrastructure.Http;

public sealed class ControlledBrowserOptions
{
    public const string SectionName = "ControlledBrowser";
    public bool Enabled { get; set; } = true;
    public int MaximumConcurrency { get; set; } = 2;
    public int NavigationTimeoutMilliseconds { get; set; } = 20_000;
    public int ActionTimeoutMilliseconds { get; set; } = 10_000;
    public int MaximumDomBytes { get; set; } = 2 * 1024 * 1024;
    public bool ChromiumSandbox { get; set; } = true;
    public string? ChromiumExecutablePath { get; set; }
}
