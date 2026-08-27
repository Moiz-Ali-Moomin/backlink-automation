using BacklinkStudio.Application;

namespace BacklinkStudio.UnitTests;

public sealed class UrlNormalizerTests
{
    private readonly UrlNormalizer _normalizer = new();

    [Theory]
    [InlineData("HTTPS://Example.COM:443/path#fragment", "https://example.com/path")]
    [InlineData("http://Example.COM:80/", "http://example.com/")]
    [InlineData("https://bücher.example/catalog", "https://xn--bcher-kva.example/catalog")]
    [InlineData("https://example.com/path/", "https://example.com/path")]
    [InlineData("https://EXAMPLE.com./a/../b/%7Euser?q=a%20b#ignored", "https://example.com/b/~user?q=a%20b")]
    [InlineData("https://example.com:8443/path", "https://example.com:8443/path")]
    public void Normalize_CanonicalizesSafeAbsoluteUrls(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("not a url")]
    [InlineData("https://example.com/%ZZ")]
    [InlineData("https:\\example.com\\path")]
    [InlineData("https://example.com:0/path")]
    public void Normalize_RejectsUnsafeOrMalformedInput(string? input)
    {
        var result = _normalizer.Normalize(input);
        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedUrl);
    }

    [Theory]
    [InlineData("../resources/#top", "https://example.com/catalog/item", "https://example.com/resources")]
    [InlineData("//Partner.Example/path/", "https://example.com/base", "https://partner.example/path")]
    [InlineData("?page=2", "https://example.com/resources", "https://example.com/resources?page=2")]
    [InlineData("/resources/", "https://example.com/catalog/item", "https://example.com/resources")]
    public void Normalize_ResolvesRelativeUrlsAgainstSafeBase(string input, string baseUrl, string expected)
    {
        var result = _normalizer.Normalize(input, new Uri(baseUrl));

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedUrl);
    }

    [Fact]
    public void Normalize_RejectsRelativeUrlWithoutBase()
    {
        Assert.False(_normalizer.Normalize("../relative").IsValid);
    }
}
