using BacklinkStudio.Application;
using BacklinkStudio.Infrastructure.Security;

namespace BacklinkStudio.UnitTests;

public sealed class ApiKeyHasherTests
{
    [Fact]
    public void Hash_IsSaltedAndVerifiableWithoutPlaintextStorage()
    {
        const string key = "bls_test_01234567890123456789012345678901";
        var hasher = new ApiKeyHasher();
        var first = hasher.Hash(key);
        var second = hasher.Hash(key);

        Assert.True(hasher.Verify(key, first.Hash, first.Salt));
        Assert.False(hasher.Verify(key + "x", first.Hash, first.Salt));
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.Equal(first.LookupHash, second.LookupHash);
        Assert.NotEqual(key, System.Text.Encoding.UTF8.GetString(first.Hash));
    }

    [Fact]
    public void Hash_RejectsLowEntropyLength()
    {
        Assert.Throws<ValidationException>(() => new ApiKeyHasher().Hash("too-short"));
    }
}
