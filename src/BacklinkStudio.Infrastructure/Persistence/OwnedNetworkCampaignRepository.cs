using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class OwnedNetworkCampaignRepository(BacklinkStudioDbContext dbContext) : IOwnedNetworkCampaignRepository
{
    public void Add(OwnedNetworkCampaignConfiguration configuration) => dbContext.OwnedNetworkCampaignConfigurations.Add(configuration);

    public Task<OwnedNetworkCampaignConfiguration?> GetAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.OwnedNetworkCampaignConfigurations : dbContext.OwnedNetworkCampaignConfigurations.AsNoTracking())
            .SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListExistingSubmissionSourceIdsAsync(Guid campaignId,
        IReadOnlyCollection<Guid> submissionSourceIds, string targetUrl, BacklinkPlacementMethod placementType,
        CancellationToken cancellationToken)
    {
        if (submissionSourceIds.Count == 0) return new HashSet<Guid>();
        var values = await dbContext.SubmissionJobs.AsNoTracking()
            .Where(x => x.CampaignId == campaignId && x.SubmissionSourceId != null &&
                submissionSourceIds.Contains(x.SubmissionSourceId.Value) && x.TargetUrl == targetUrl &&
                x.PlacementType == placementType)
            .Select(x => x.SubmissionSourceId!.Value)
            .ToListAsync(cancellationToken);
        return values.ToHashSet();
    }

    public async Task<OwnedNetworkSubmissionWorkItem?> GetWorkItemAsync(Guid persistentJobId, CancellationToken cancellationToken)
    {
        var row = await (from submission in dbContext.SubmissionJobs
                         join campaign in dbContext.Campaigns on submission.CampaignId equals campaign.Id
                         join configuration in dbContext.OwnedNetworkCampaignConfigurations on campaign.Id equals configuration.CampaignId
                         join source in dbContext.SubmissionSources on submission.SubmissionSourceId equals source.Id
                         where submission.PersistentJobId == persistentJobId
                         select new { submission, campaign, configuration, source }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.submission, row.campaign, row.configuration, row.source);
    }

    public Task<int> CountSuccessfulActionsAsync(Guid projectId, Guid? campaignId, string? domain, DateTimeOffset since,
        CancellationToken cancellationToken) =>
        (from submission in dbContext.SubmissionJobs.AsNoTracking()
         join source in dbContext.SubmissionSources.AsNoTracking() on submission.SubmissionSourceId equals source.Id
         where submission.ProjectId == projectId && (campaignId == null || submission.CampaignId == campaignId) &&
               (domain == null || source.Domain == domain) && submission.UpdatedAt >= since &&
               (submission.Status == SubmissionStatus.Submitted || submission.Status == SubmissionStatus.PendingModeration || submission.Status == SubmissionStatus.Approved)
         select submission).CountAsync(cancellationToken);
}
