using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BacklinkStudio.Infrastructure.Persistence;

public interface IDatabaseInitializer
{
    Task MigrateAndSeedAsync(CancellationToken cancellationToken);
}

internal sealed class DatabaseInitializer(
    BacklinkStudioDbContext dbContext,
    IApiKeyHasher hasher,
    IConfiguration configuration,
    TimeProvider timeProvider) : IDatabaseInitializer
{
    public Task MigrateAndSeedAsync(CancellationToken cancellationToken) =>
        DatabaseOperations.MigrateAndSeedAsync(dbContext, hasher, configuration, timeProvider, cancellationToken);
}

public static class DatabaseOperations
{
    public static async Task MigrateAndSeedAsync(
        BacklinkStudioDbContext dbContext,
        IApiKeyHasher hasher,
        IConfiguration configuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        var apiKey = configuration["BacklinkStudio:BootstrapApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("BacklinkStudio:BootstrapApiKey is required when migrating a new environment.");
        }

        var lookup = hasher.LookupHash(apiKey);
        if (await dbContext.AgentCredentials.AnyAsync(x => x.LookupHash == lookup, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var displayName = configuration["BacklinkStudio:BootstrapName"] ?? "Bootstrap Administrator";
        var user = new User(displayName, now);
        var keyHash = hasher.Hash(apiKey);
        var credential = new AgentCredential(user.Id, displayName, keyHash.LookupHash, keyHash.Hash, keyHash.Salt, AuthorizationScopes.All, null, now);
        dbContext.Users.Add(user);
        dbContext.AgentCredentials.Add(credential);
        dbContext.AuditEvents.Add(new AuditEvent(ActorType.System, "migration", null, "credential.bootstrap", null, null, null, "migration", $"credentialId={credential.Id};name={displayName}", "succeeded", null, now));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
