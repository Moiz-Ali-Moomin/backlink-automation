using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class SubmissionContentRepository(BacklinkStudioDbContext dbContext) : ISubmissionContentRepository, IWordPressSiteProfileRepository
{
    public void AddIdentityPool(SubmissionIdentityPool pool) => dbContext.SubmissionIdentityPools.Add(pool);
    public void AddIdentity(SubmissionIdentity identity) => dbContext.SubmissionIdentities.Add(identity);
    public void AddTemplatePool(SubmissionTemplatePool pool) => dbContext.SubmissionTemplatePools.Add(pool);
    public void AddTemplate(SubmissionTemplate submissionTemplate) => dbContext.SubmissionTemplates.Add(submissionTemplate);
    public void Add(WordPressSiteProfile profile) => dbContext.WordPressSiteProfiles.Add(profile);

    public Task<SubmissionIdentityPool?> GetIdentityPoolAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        Query(dbContext.SubmissionIdentityPools, tracked).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubmissionIdentityPool?> FindIdentityPoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken) =>
        dbContext.SubmissionIdentityPools.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Name == name, cancellationToken);

    public Task<SubmissionIdentity?> GetIdentityAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        Query(dbContext.SubmissionIdentities, tracked).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubmissionTemplatePool?> GetTemplatePoolAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        Query(dbContext.SubmissionTemplatePools, tracked).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubmissionTemplatePool?> FindTemplatePoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken) =>
        dbContext.SubmissionTemplatePools.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Name == name, cancellationToken);

    public Task<SubmissionTemplate?> GetTemplateAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        Query(dbContext.SubmissionTemplates, tracked).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        Query(dbContext.WordPressSiteProfiles, tracked).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) =>
        dbContext.WordPressSiteProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.OwnedNetworkProfileId == ownedNetworkProfileId && x.Domain == domain && x.Enabled, cancellationToken);

    public Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken) =>
        dbContext.WordPressSiteProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.OwnedNetworkProfileId == ownedNetworkProfileId && x.Domain == domain, cancellationToken);

    public async Task<IReadOnlyList<SubmissionIdentityPool>> ListIdentityPoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken) =>
        await ApplyCursor(dbContext.SubmissionIdentityPools.AsNoTracking().Where(x => x.ProjectId == projectId), cursor)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubmissionIdentity>> ListIdentitiesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.SubmissionIdentities.AsNoTracking().Where(x => x.PoolId == poolId);
        if (enabledOnly) query = query.Where(x => x.Enabled);
        return await ApplyCursor(query, cursor).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubmissionTemplatePool>> ListTemplatePoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken) =>
        await ApplyCursor(dbContext.SubmissionTemplatePools.AsNoTracking().Where(x => x.ProjectId == projectId), cursor)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubmissionTemplate>> ListTemplatesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.SubmissionTemplates.AsNoTracking().Where(x => x.PoolId == poolId);
        if (enabledOnly) query = query.Where(x => x.Enabled);
        return await ApplyCursor(query, cursor).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken) =>
        await ApplyCursor(dbContext.WordPressSiteProfiles.AsNoTracking().Where(x => x.OwnedNetworkProfileId == ownedNetworkProfileId), cursor)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);

    private static IQueryable<T> Query<T>(DbSet<T> set, bool tracked) where T : class => tracked ? set : set.AsNoTracking();

    private static IQueryable<T> ApplyCursor<T>(IQueryable<T> query, PageCursor? cursor) where T : class
    {
        if (cursor is null) return query;
        var value = cursor.Value;
        return query.Where(x => EF.Property<DateTimeOffset>(x, "CreatedAt") > value.CreatedAt ||
            EF.Property<DateTimeOffset>(x, "CreatedAt") == value.CreatedAt && EF.Property<Guid>(x, "Id").CompareTo(value.Id) > 0);
    }
}
