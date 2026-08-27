namespace BacklinkStudio.Infrastructure.Http;

public sealed class WordPressFallbackOptions
{
    public const string SectionName = "WordPressFallback";
    public int MaximumStrategies { get; set; } = 4;
}
