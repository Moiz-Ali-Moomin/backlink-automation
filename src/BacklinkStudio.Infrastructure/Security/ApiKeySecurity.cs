using System.Security.Cryptography;
using System.Text;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Security;

public sealed record ApiKeyHash(string LookupHash, byte[] Hash, byte[] Salt);
public sealed record AuthenticatedCredential(Guid CredentialId, Guid UserId, string Name, IReadOnlyList<string> Scopes);

public interface IApiKeyHasher
{
    ApiKeyHash Hash(string apiKey);
    string LookupHash(string apiKey);
    bool Verify(string apiKey, byte[] expectedHash, byte[] salt);
}

public sealed class ApiKeyHasher : IApiKeyHasher
{
    private const int Iterations = 210_000;
    private const int HashSize = 64;
    private const int SaltSize = 32;

    public ApiKeyHash Hash(string apiKey)
    {
        Validate(apiKey);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new ApiKeyHash(LookupHash(apiKey), Derive(apiKey, salt), salt);
    }

    public string LookupHash(string apiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public bool Verify(string apiKey, byte[] expectedHash, byte[] salt)
    {
        if (string.IsNullOrEmpty(apiKey) || expectedHash.Length != HashSize || salt.Length != SaltSize)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Derive(apiKey, salt), expectedHash);
    }

    private static byte[] Derive(string apiKey, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(apiKey), salt, Iterations, HashAlgorithmName.SHA512, HashSize);

    private static void Validate(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32 || apiKey.Length > 512)
        {
            throw new ValidationException("API keys must contain between 32 and 512 characters.");
        }
    }
}

public sealed class AgentApiKeyFactory(IApiKeyHasher hasher) : IAgentApiKeyFactory
{
    public AgentApiKeyMaterial Create()
    {
        var plaintext = $"bls_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}";
        var material = hasher.Hash(plaintext);
        return new AgentApiKeyMaterial(plaintext, material.LookupHash, material.Hash, material.Salt);
    }
}

public interface ICredentialAuthenticator
{
    Task<AuthenticatedCredential?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken);
}

public sealed class CredentialAuthenticator(BacklinkStudioDbContext dbContext, IApiKeyHasher hasher, TimeProvider timeProvider) : ICredentialAuthenticator
{
    public async Task<AuthenticatedCredential?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 512)
        {
            return null;
        }

        var lookup = hasher.LookupHash(apiKey);
        var credential = await dbContext.AgentCredentials.SingleOrDefaultAsync(x => x.LookupHash == lookup, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (credential is null || !credential.IsActive(now) || !hasher.Verify(apiKey, credential.KeyHash, credential.KeySalt))
        {
            return null;
        }

        var userEnabled = await dbContext.Users.AnyAsync(
            user => user.Id == credential.UserId && user.Enabled,
            cancellationToken);
        if (!userEnabled)
        {
            return null;
        }

        credential.RecordUse(now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(credential).State = EntityState.Detached;
            return null;
        }

        return new AuthenticatedCredential(credential.Id, credential.UserId, credential.Name, credential.Scopes);
    }
}
