using BacklinkStudio.Application;

namespace BacklinkStudio.UnitTests;

public sealed class PaginationTests
{
    [Fact]
    public void Cursor_RoundTripsAndIsOpaque()
    {
        var value = new PageCursor(new DateTimeOffset(2026, 8, 18, 1, 2, 3, TimeSpan.Zero), Guid.Parse("0198b900-0000-7000-8000-000000000001"));
        var encoded = CursorCodec.Encode(value);
        Assert.DoesNotContain(":", encoded);
        Assert.Equal(value, CursorCodec.Decode(encoded));
    }

    [Fact]
    public void Cursor_RejectsMalformedInput() => Assert.Throws<ValidationException>(() => CursorCodec.Decode("not-base64***"));

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(5000, 100)]
    [InlineData(25, 25)]
    public void PageLimit_IsBounded(int input, int expected) => Assert.Equal(expected, new PageRequest(input).BoundedLimit);
}
