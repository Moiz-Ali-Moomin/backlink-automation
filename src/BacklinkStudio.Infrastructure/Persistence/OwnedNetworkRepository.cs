using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class OwnedNetworkRepository(BacklinkStudioDbContext dbContext) : IOwnedNetworkRepository
{
    public void Add(OwnedNetworkProfile profile) => dbContext.OwnedNetworkProfiles.Add(profile);
    public void AddDomain(OwnedNetworkDomain domain) => dbContext.OwnedNetworkDomains.Add(domain);

    public Task<OwnedNetworkProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.OwnedNetworkProfiles : dbContext.OwnedNetworkProfiles.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<OwnedNetworkProfile?> FindByNameAsync(Guid projectId, string name, CancellationToken cancellationToken) =>
        dbContext.OwnedNetworkProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Name == name, cancellationToken);

    public async Task<IReadOnlyList<OwnedNetworkProfile>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.OwnedNetworkProfiles.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            var value = cursor.Value;
            query = query.Where(x => x.CreatedAt > value.CreatedAt || x.CreatedAt == value.CreatedAt && x.Id.CompareTo(value.Id) > 0);
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OwnedNetworkDomain>> ListDomainsAsync(Guid ownedNetworkProfileId, bool tracked, CancellationToken cancellationToken) =>
        await (tracked ? dbContext.OwnedNetworkDomains : dbContext.OwnedNetworkDomains.AsNoTracking())
            .Where(x => x.OwnedNetworkProfileId == ownedNetworkProfileId)
            .OrderBy(x => x.Domain).ThenBy(x => x.MatchType)
            .ToListAsync(cancellationToken);

    public void RemoveDomains(IEnumerable<OwnedNetworkDomain> domains) => dbContext.OwnedNetworkDomains.RemoveRange(domains);
}
