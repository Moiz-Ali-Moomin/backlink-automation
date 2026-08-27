namespace BacklinkStudio.Infrastructure.Http;

public sealed class WordPressSubmissionOptions
{
    public const string SectionName = "WordPressSubmission";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaximumResponseBytes { get; set; } = 2 * 1_024 * 1_024;
    public bool AllowInsecureLoopbackHttpForTesting { get; set; }
    public string[] AllowedInsecureControlledHttpHosts { get; set; } = [];
    public Dictionary<string, WordPressCredentialOptions> Credentials { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WordPressCredentialOptions
{
    public string Username { get; set; } = string.Empty;
    public string ApplicationPassword { get; set; } = string.Empty;
}
