using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class StudioRepository(BacklinkStudioDbContext dbContext) :
    IProjectRepository,
    ICandidateRepository,
    IOpportunityRepository,
    IPolicyRepository,
    IDiscoveryRepository,
    ISubmissionRepository,
    IBacklinkRepository,
    IReportRepository,
    IReportDataSource,
    IAgentCredentialRepository,
    IAuditSink,
    IIdempotencyStore,
    IStudioUnitOfWork
{
    void IAgentCredentialRepository.Add(User user) => dbContext.Users.Add(user);
    void IAgentCredentialRepository.Add(AgentCredential credential) => dbContext.AgentCredentials.Add(credential);
    Task<AgentCredential?> IAgentCredentialRepository.GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.AgentCredentials : dbContext.AgentCredentials.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    async Task<IReadOnlyList<AgentCredential>> IAgentCredentialRepository.ListAsync(PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.AgentCredentials.AsNoTracking();
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public void Add(Project project) => dbContext.Projects.Add(project);
    Task<Project?> IProjectRepository.GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Projects.AsNoTracking();
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public void AddTarget(ProjectTarget target) => dbContext.ProjectTargets.Add(target);

    public async Task<IReadOnlyList<ProjectTarget>> ListTargetsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.ProjectTargets.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectTarget>> ListEnabledTargetsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProjectTargets.AsNoTracking().Where(x => x.ProjectId == projectId && x.Enabled).OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public Task<ProjectTarget?> GetTargetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectTargets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<CandidateImportPersistenceResult> ImportAsync(Guid projectId, IReadOnlyList<CandidateImportItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new CandidateImportPersistenceResult(0, 0);
        }

        var normalizedUrls = items.Select(x => x.NormalizedUrl).ToArray();
        var existingUrls = await dbContext.CandidatePages
            .Where(x => x.ProjectId == projectId && normalizedUrls.Contains(x.NormalizedUrl))
            .Select(x => x.NormalizedUrl)
            .ToHashSetAsync(cancellationToken);

        var domains = items.Select(x => x.Domain).Distinct(StringComparer.Ordinal).ToArray();
        var sites = await dbContext.CandidateSites
            .Where(x => x.ProjectId == projectId && domains.Contains(x.Domain))
            .ToDictionaryAsync(x => x.Domain, StringComparer.Ordinal, cancellationToken);

        var accepted = 0;
        foreach (var item in items)
        {
            if (existingUrls.Contains(item.NormalizedUrl))
            {
                continue;
            }

            if (!sites.TryGetValue(item.Domain, out var site))
            {
                site = new CandidateSite(projectId, item.Domain, now);
                sites.Add(item.Domain, site);
                dbContext.CandidateSites.Add(site);
            }

            dbContext.CandidatePages.Add(new CandidatePage(projectId, site.Id, item.OriginalUrl, item.NormalizedUrl, now));
            accepted++;
        }

        return new CandidateImportPersistenceResult(accepted, items.Count - accepted);
    }

    public async Task<IReadOnlyList<CandidateDto>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var pages = dbContext.CandidatePages.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            pages = pages.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await (
            from page in pages
            join site in dbContext.CandidateSites.AsNoTracking() on page.CandidateSiteId equals site.Id
            orderby page.CreatedAt, page.Id
            select new CandidateDto(page.Id, page.ProjectId, page.CandidateSiteId, page.Url, page.NormalizedUrl, site.Domain, page.AnalysisStatus, page.FinalUrl, page.HttpStatus, page.ContentType, page.Title, page.CanonicalUrl, page.Cms, page.RobotsDirectives, page.ExistingTargetLink, page.EligibleSignals, page.RequiresJavaScript, page.AnalysisError, page.LastAnalyzedAt, page.CreatedAt))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<CandidateDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (from page in dbContext.CandidatePages.AsNoTracking()
         join site in dbContext.CandidateSites.AsNoTracking() on page.CandidateSiteId equals site.Id
         where page.Id == id
         select new CandidateDto(page.Id, page.ProjectId, page.CandidateSiteId, page.Url, page.NormalizedUrl, site.Domain, page.AnalysisStatus, page.FinalUrl, page.HttpStatus, page.ContentType, page.Title, page.CanonicalUrl, page.Cms, page.RobotsDirectives, page.ExistingTargetLink, page.EligibleSignals, page.RequiresJavaScript, page.AnalysisError, page.LastAnalyzedAt, page.CreatedAt))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CandidateForAnalysis>> GetUnanalyzedAsync(Guid projectId, int take, CancellationToken cancellationToken) =>
        await (
            from page in dbContext.CandidatePages
            join site in dbContext.CandidateSites.AsNoTracking() on page.CandidateSiteId equals site.Id
            where page.ProjectId == projectId && page.AnalysisStatus == CandidateAnalysisStatus.New
            orderby page.CreatedAt, page.Id
            select new CandidateForAnalysis(page, site.Domain))
            .Take(take)
            .ToListAsync(cancellationToken);

    public void ApplyAnalysis(CandidatePage page, SiteAnalysisResult analysis, bool existingTargetLink)
    {
        if (dbContext.Entry(page).State == EntityState.Detached)
        {
            dbContext.CandidatePages.Attach(page);
        }

        page.ApplyAnalysis(analysis.FinalUrl, analysis.HttpStatus, analysis.ContentType, analysis.Title, analysis.CanonicalUrl, analysis.Cms, analysis.RobotsDirectives, existingTargetLink, analysis.EligibleSignals, analysis.RequiresJavaScript, analysis.Error, analysis.AnalyzedAt);
    }

    public void MarkBlocked(CandidatePage page, string reason, DateTimeOffset analyzedAt)
    {
        if (dbContext.Entry(page).State == EntityState.Detached)
        {
            dbContext.CandidatePages.Attach(page);
        }
        page.MarkBlocked(reason, analyzedAt);
    }

    public Task<bool> ExistsForCandidateAsync(Guid candidatePageId, CancellationToken cancellationToken) =>
        dbContext.Opportunities.AnyAsync(x => x.CandidatePageId == candidatePageId, cancellationToken);

    public void Add(Opportunity opportunity) => dbContext.Opportunities.Add(opportunity);

    async Task<OpportunityDto?> IOpportunityRepository.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var value = await dbContext.Opportunities.AsNoTracking().Include(x => x.ScoreReasons).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return value?.ToDto();
    }

    public async Task<IReadOnlyList<OpportunityDto>> ListAsync(Guid projectId, int? minimumQuality, int? maximumRisk, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Opportunities.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (minimumQuality.HasValue)
        {
            query = query.Where(x => x.QualityScore >= minimumQuality.Value);
        }
        if (maximumRisk.HasValue)
        {
            query = query.Where(x => x.RiskScore <= maximumRisk.Value);
        }

        if (cursor is not null)
        {
            query = query.Where(x => x.DetectedAt > cursor.Value.CreatedAt || (x.DetectedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        var values = await query.Include(x => x.ScoreReasons).OrderBy(x => x.DetectedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
        return values.Select(x => x.ToDto()).ToArray();
    }

    public async Task<OpportunitySummaryDto> SummaryAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.Opportunities.AsNoTracking().Where(x => x.ProjectId == projectId);
        var typeCounts = await baseQuery.GroupBy(x => x.Type).Select(x => new { Key = x.Key, Count = x.Count() }).ToListAsync(cancellationToken);
        var statusCounts = await baseQuery.GroupBy(x => x.AutomationStatus).Select(x => new { Key = x.Key, Count = x.Count() }).ToListAsync(cancellationToken);
        var byType = typeCounts.ToDictionary(x => x.Key.ToString(), x => x.Count);
        var byStatus = statusCounts.ToDictionary(x => x.Key.ToString(), x => x.Count);
        return new OpportunitySummaryDto(byType.Values.Sum(), byType, byStatus);
    }

    public async Task<IReadOnlyList<Opportunity>> GetTrackedAsync(Guid projectId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.Opportunities.Where(x => x.ProjectId == projectId && ids.Contains(x.Id)).ToListAsync(cancellationToken);

    public void Append(AuditEvent auditEvent) => dbContext.AuditEvents.Add(auditEvent);
    public Task<PolicyDefinition?> GetAsync(Guid projectId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? dbContext.PolicyDefinitions : dbContext.PolicyDefinitions.AsNoTracking();
        return query.SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
    }
    public void Add(PolicyDefinition policy) => dbContext.PolicyDefinitions.Add(policy);
    public async Task<IReadOnlyList<BlocklistEntry>> ListBlocklistAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.BlocklistEntries.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<BlocklistEntry>> ListEnabledBlocklistAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.BlocklistEntries.AsNoTracking().Where(x => x.ProjectId == projectId && x.Enabled).ToListAsync(cancellationToken);
    public Task<BlocklistEntry?> FindBlocklistAsync(Guid projectId, BlocklistMatchType matchType, string value, CancellationToken cancellationToken) =>
        dbContext.BlocklistEntries.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.MatchType == matchType && x.Value == value, cancellationToken);
    public void Add(BlocklistEntry entry) => dbContext.BlocklistEntries.Add(entry);
    public void Add(DiscoveryQuery query) => dbContext.DiscoveryQueries.Add(query);
    public void Add(DiscoveryRun run) => dbContext.DiscoveryRuns.Add(run);
    public async Task<(DiscoveryRun Run, DiscoveryQuery Query)?> GetTrackedAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.DiscoveryRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        var query = await dbContext.DiscoveryQueries.AsNoTracking().SingleAsync(x => x.Id == run.DiscoveryQueryId, cancellationToken);
        return (run, query);
    }

    async Task<DiscoveryRunDto?> IDiscoveryRepository.GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var value = await (
            from run in dbContext.DiscoveryRuns.AsNoTracking()
            join query in dbContext.DiscoveryQueries.AsNoTracking() on run.DiscoveryQueryId equals query.Id
            where run.Id == runId
            select new { Run = run, Query = query })
            .SingleOrDefaultAsync(cancellationToken);
        return value is null ? null : value.Run.ToDto(value.Query);
    }

    async Task<IReadOnlyList<DiscoveryRunDto>> IDiscoveryRepository.ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var runs = dbContext.DiscoveryRuns.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            runs = runs.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        var values = await (
            from run in runs
            join query in dbContext.DiscoveryQueries.AsNoTracking() on run.DiscoveryQueryId equals query.Id
            orderby run.CreatedAt, run.Id
            select new { Run = run, Query = query })
            .Take(take)
            .ToListAsync(cancellationToken);
        return values.Select(x => x.Run.ToDto(x.Query)).ToArray();
    }
    public Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken) => dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
    public void Add(IdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The resource was changed by another request. Reload it and retry with a new request key.");
        }
    }

    public void AddCampaign(Campaign campaign) => dbContext.Campaigns.Add(campaign);
    public void AddCampaignTarget(CampaignTarget target) => dbContext.CampaignTargets.Add(target);
    public void AddCampaignOpportunity(CampaignOpportunity opportunity) => dbContext.CampaignOpportunities.Add(opportunity);
    public void AddSubmission(SubmissionJob submission) => dbContext.SubmissionJobs.Add(submission);
    public void AddAttempt(SubmissionAttempt attempt) => dbContext.SubmissionAttempts.Add(attempt);
    public Task<Campaign?> GetCampaignAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.Campaigns : dbContext.Campaigns.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<Campaign>> ListCampaignsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Campaigns.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null) query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<CampaignOpportunity>> ListCampaignOpportunitiesAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? dbContext.CampaignOpportunities : dbContext.CampaignOpportunities.AsNoTracking();
        return await query.Where(x => x.CampaignId == campaignId).OrderBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<SubmissionJob>> ListCampaignSubmissionJobsAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? dbContext.SubmissionJobs : dbContext.SubmissionJobs.AsNoTracking();
        return await query.Where(x => x.CampaignId == campaignId).OrderBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<PersistentJob>> ListCampaignJobsAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? dbContext.Jobs : dbContext.Jobs.AsNoTracking();
        return await query.Where(x => x.CampaignId == campaignId && x.Type == JobType.Submission).OrderBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
    }
    public async Task<int> PauseCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await dbContext.Jobs.Where(x => x.CampaignId == campaignId &&
            (x.Status == JobStatus.Queued || x.Status == JobStatus.RetryScheduled)).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatus.Paused)
                .SetProperty(x => x.PausedAt, now)
                .SetProperty(x => x.PauseRequestedAt, (DateTimeOffset?)null), cancellationToken);
        var active = await dbContext.Jobs.Where(x => x.CampaignId == campaignId &&
            (x.Status == JobStatus.Claimed || x.Status == JobStatus.Running) && x.PauseRequestedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PauseRequestedAt, now), cancellationToken);
        return pending + active;
    }
    public async Task<int> ResumeCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var paused = await dbContext.Jobs.Where(x => x.CampaignId == campaignId && x.Status == JobStatus.Paused)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatus.RetryScheduled)
                .SetProperty(x => x.AvailableAt, now)
                .SetProperty(x => x.PauseRequestedAt, (DateTimeOffset?)null)
                .SetProperty(x => x.PausedAt, (DateTimeOffset?)null), cancellationToken);
        var active = await dbContext.Jobs.Where(x => x.CampaignId == campaignId &&
            (x.Status == JobStatus.Claimed || x.Status == JobStatus.Running) && x.PauseRequestedAt != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PauseRequestedAt, (DateTimeOffset?)null), cancellationToken);
        return paused + active;
    }
    public async Task<int> StopCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await dbContext.Jobs.Where(x => x.CampaignId == campaignId &&
            (x.Status == JobStatus.Queued || x.Status == JobStatus.RetryScheduled || x.Status == JobStatus.Paused))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatus.Cancelled)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.WorkerId, (string?)null)
                .SetProperty(x => x.ClaimExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.PauseRequestedAt, (DateTimeOffset?)null)
                .SetProperty(x => x.PausedAt, (DateTimeOffset?)null), cancellationToken);
        var active = await dbContext.Jobs.Where(x => x.CampaignId == campaignId &&
            (x.Status == JobStatus.Claimed || x.Status == JobStatus.Running) && x.PauseRequestedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PauseRequestedAt, now), cancellationToken);
        await dbContext.SubmissionJobs.Where(x => x.CampaignId == campaignId && x.Status == SubmissionStatus.Queued &&
            dbContext.Jobs.Any(job => job.Id == x.PersistentJobId && job.Status == JobStatus.Cancelled))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, SubmissionStatus.Cancelled).SetProperty(x => x.UpdatedAt, now), cancellationToken);
        return pending + active;
    }
    public Task<bool> SubmissionExistsAsync(Guid campaignOpportunityId, CancellationToken cancellationToken) =>
        dbContext.SubmissionJobs.AnyAsync(x => x.CampaignOpportunityId == campaignOpportunityId, cancellationToken);
    public Task<bool> HasSuccessfulSubmissionAsync(Guid projectId, Guid opportunityId, Guid excludingSubmissionId, CancellationToken cancellationToken) =>
        (from submission in dbContext.SubmissionJobs.AsNoTracking()
         join campaignOpportunity in dbContext.CampaignOpportunities.AsNoTracking() on submission.CampaignOpportunityId equals campaignOpportunity.Id
         where submission.ProjectId == projectId && campaignOpportunity.OpportunityId == opportunityId && submission.Id != excludingSubmissionId && (submission.Status == SubmissionStatus.Submitted || submission.Status == SubmissionStatus.PendingModeration)
         select submission).AnyAsync(cancellationToken);
    public async Task<SubmissionWorkItem?> GetWorkItemAsync(Guid persistentJobId, CancellationToken cancellationToken)
    {
        var row = await (from submission in dbContext.SubmissionJobs
                         join campaign in dbContext.Campaigns on submission.CampaignId equals campaign.Id
                         join campaignOpportunity in dbContext.CampaignOpportunities on submission.CampaignOpportunityId equals campaignOpportunity.Id
                         join opportunity in dbContext.Opportunities on campaignOpportunity.OpportunityId equals opportunity.Id
                         join campaignTarget in dbContext.CampaignTargets on campaign.Id equals campaignTarget.CampaignId
                         join target in dbContext.ProjectTargets on campaignTarget.ProjectTargetId equals target.Id
                         where submission.PersistentJobId == persistentJobId
                         select new { submission, campaign, campaignOpportunity, opportunity, target }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new SubmissionWorkItem(row.submission, row.campaign, row.campaignOpportunity, row.opportunity, row.target);
    }
    public Task<int> CountSuccessfulActionsAsync(Guid projectId, Guid? campaignId, string? domain, DateTimeOffset since, CancellationToken cancellationToken) =>
        (from submission in dbContext.SubmissionJobs.AsNoTracking()
         join campaignOpportunity in dbContext.CampaignOpportunities.AsNoTracking() on submission.CampaignOpportunityId equals campaignOpportunity.Id
         join opportunity in dbContext.Opportunities.AsNoTracking() on campaignOpportunity.OpportunityId equals opportunity.Id
         where submission.ProjectId == projectId && (campaignId == null || submission.CampaignId == campaignId) && (domain == null || opportunity.Domain == domain) && submission.UpdatedAt >= since && (submission.Status == SubmissionStatus.Submitted || submission.Status == SubmissionStatus.PendingModeration)
         select submission).CountAsync(cancellationToken);
    public Task<SubmissionJob?> GetSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.SubmissionJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<SubmissionJob>> ListSubmissionsAsync(Guid campaignId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.SubmissionJobs.AsNoTracking().Where(x => x.CampaignId == campaignId);
        if (cursor is not null) query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<SubmissionAttempt>> ListAttemptsAsync(Guid submissionJobId, int take, CancellationToken cancellationToken) =>
        await dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submissionJobId).OrderBy(x => x.AttemptNumber).Take(take).ToListAsync(cancellationToken);

    public void Add(Backlink backlink) => dbContext.Backlinks.Add(backlink);
    public void AddCheck(VerificationCheck check) => dbContext.VerificationChecks.Add(check);
    public Task<Backlink?> FindBySubmissionAsync(Guid submissionJobId, CancellationToken cancellationToken) =>
        dbContext.Backlinks.SingleOrDefaultAsync(x => x.SubmissionJobId == submissionJobId, cancellationToken);
    public Task<Backlink?> FindBySourceTargetAsync(Guid projectId, string normalizedSourceUrl, string normalizedTargetUrl, CancellationToken cancellationToken) =>
        dbContext.Backlinks.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.NormalizedSourceUrl == normalizedSourceUrl && x.NormalizedTargetUrl == normalizedTargetUrl, cancellationToken);
    public Task<Backlink?> GetBacklinkAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.Backlinks : dbContext.Backlinks.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<Backlink>> ListAsync(Guid projectId, BacklinkStatus? status, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Backlinks.AsNoTracking().Where(x => x.ProjectId == projectId && (status == null || x.Status == status));
        if (cursor is not null) query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<VerificationCheck>> ListHistoryAsync(Guid backlinkId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.VerificationChecks.AsNoTracking().Where(x => x.BacklinkId == backlinkId);
        if (cursor is not null) query = query.Where(x => x.CheckedAt > cursor.Value.CreatedAt || (x.CheckedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        return await query.OrderBy(x => x.CheckedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    void IReportRepository.Add(Report report) => dbContext.Reports.Add(report);

    Task<Report?> IReportRepository.GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.Reports : dbContext.Reports.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    async Task<IReadOnlyList<Report>> IReportRepository.ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Reports.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    async Task<ReportSummaryDto> IReportDataSource.GetSummaryAsync(Guid projectId, Guid? campaignId, ReportKind kind, CancellationToken cancellationToken)
    {
        var candidates = 0;
        var eligible = 0;
        var approved = 0;
        if (kind == ReportKind.CampaignPerformance && campaignId is not null)
        {
            var opportunityCounts = await dbContext.CampaignOpportunities.AsNoTracking()
                .Where(x => x.CampaignId == campaignId)
                .GroupBy(_ => 1)
                .Select(group => new { Candidates = group.Count(), Approved = group.Count(x => x.ExplicitlyApproved) })
                .SingleOrDefaultAsync(cancellationToken);
            candidates = opportunityCounts?.Candidates ?? 0;
            eligible = candidates;
            approved = opportunityCounts?.Approved ?? 0;
            if (candidates == 0 && await dbContext.OwnedNetworkCampaignConfigurations.AsNoTracking()
                    .AnyAsync(x => x.CampaignId == campaignId, cancellationToken))
            {
                candidates = await dbContext.SubmissionJobs.AsNoTracking().CountAsync(x => x.CampaignId == campaignId, cancellationToken);
                eligible = candidates;
                approved = candidates;
            }
        }

        var submissionQuery = dbContext.SubmissionJobs.AsNoTracking()
            .Where(x => x.ProjectId == projectId && (campaignId == null || x.CampaignId == campaignId));
        var submissionCounts = await submissionQuery.GroupBy(_ => 1).Select(group => new
        {
            Queued = group.Count(x => x.Status == SubmissionStatus.Queued),
            Processing = group.Count(x => x.Status == SubmissionStatus.Processing),
            Submitted = group.Count(x => x.Status == SubmissionStatus.Submitted),
            Pending = group.Count(x => x.Status == SubmissionStatus.PendingModeration),
            Approved = group.Count(x => x.Status == SubmissionStatus.Approved),
            Duplicate = group.Count(x => x.Status == SubmissionStatus.Duplicate),
            Rejected = group.Count(x => x.Status == SubmissionStatus.Rejected),
            Failed = group.Count(x => x.Status == SubmissionStatus.Failed || x.Status == SubmissionStatus.Cancelled || x.Status == SubmissionStatus.ManualActionRequired)
        }).SingleOrDefaultAsync(cancellationToken);

        var backlinkQuery = dbContext.Backlinks.AsNoTracking()
            .Where(x => x.ProjectId == projectId && (campaignId == null || x.CampaignId == campaignId));
        var backlinkCounts = await backlinkQuery.GroupBy(_ => 1).Select(group => new
        {
            Total = group.Count(),
            Verified = group.Count(x => x.Status == BacklinkStatus.Verified),
            Pending = group.Count(x => x.Status == BacklinkStatus.PendingVerification),
            Missing = group.Count(x => x.Status == BacklinkStatus.Missing),
            Error = group.Count(x => x.Status == BacklinkStatus.Error),
            Lost = group.Count(x => x.Status == BacklinkStatus.Lost),
            Follow = group.Count(x => x.Status == BacklinkStatus.Verified && !x.Nofollow),
            Nofollow = group.Count(x => x.Status == BacklinkStatus.Verified && x.Nofollow),
            Ugc = group.Count(x => x.Status == BacklinkStatus.Verified && x.Ugc),
            Sponsored = group.Count(x => x.Status == BacklinkStatus.Verified && x.Sponsored)
        }).SingleOrDefaultAsync(cancellationToken);

        var attemptQuery = dbContext.SubmissionAttempts.AsNoTracking()
            .Where(x => x.ProjectId == projectId && (campaignId == null || x.CampaignId == campaignId));
        var attemptCounts = await attemptQuery.GroupBy(_ => 1).Select(group => new
        {
            Total = group.Count(),
            Successful = group.Count(x => x.Result == SubmissionStatus.Submitted ||
                x.Result == SubmissionStatus.PendingModeration || x.Result == SubmissionStatus.Approved)
        }).SingleOrDefaultAsync(cancellationToken);

        var domainRows = await (
                from attempt in attemptQuery
                join source in dbContext.SubmissionSources.AsNoTracking()
                    on attempt.SubmissionSourceId equals (Guid?)source.Id
                join backlinkValue in backlinkQuery on attempt.SubmissionJobId equals backlinkValue.SubmissionJobId into backlinks
                from backlink in backlinks.DefaultIfEmpty()
                group new { attempt, backlink } by source.Domain into grouped
                select new
                {
                    Key = grouped.Key,
                    Attempts = grouped.Count(),
                    Successful = grouped.Count(x => x.attempt.Result == SubmissionStatus.Submitted ||
                        x.attempt.Result == SubmissionStatus.PendingModeration || x.attempt.Result == SubmissionStatus.Approved),
                    Verified = grouped.Count(x => x.backlink != null && x.backlink.Status == BacklinkStatus.Verified)
                })
            .OrderByDescending(x => x.Attempts).ThenBy(x => x.Key).Take(100).ToListAsync(cancellationToken);
        var templateRows = await (
                from attempt in attemptQuery
                join template in dbContext.SubmissionTemplates.AsNoTracking()
                    on attempt.TemplateId equals (Guid?)template.Id
                join backlinkValue in backlinkQuery on attempt.SubmissionJobId equals backlinkValue.SubmissionJobId into backlinks
                from backlink in backlinks.DefaultIfEmpty()
                group new { attempt, backlink } by template.Name into grouped
                select new
                {
                    Key = grouped.Key,
                    Attempts = grouped.Count(),
                    Successful = grouped.Count(x => x.attempt.Result == SubmissionStatus.Submitted ||
                        x.attempt.Result == SubmissionStatus.PendingModeration || x.attempt.Result == SubmissionStatus.Approved),
                    Verified = grouped.Count(x => x.backlink != null && x.backlink.Status == BacklinkStatus.Verified)
                })
            .OrderByDescending(x => x.Attempts).ThenBy(x => x.Key).Take(100).ToListAsync(cancellationToken);
        var identityRows = await (
                from attempt in attemptQuery
                join identity in dbContext.SubmissionIdentities.AsNoTracking()
                    on attempt.IdentityId equals (Guid?)identity.Id
                join backlinkValue in backlinkQuery on attempt.SubmissionJobId equals backlinkValue.SubmissionJobId into backlinks
                from backlink in backlinks.DefaultIfEmpty()
                group new { attempt, backlink } by identity.DisplayName into grouped
                select new
                {
                    Key = grouped.Key,
                    Attempts = grouped.Count(),
                    Successful = grouped.Count(x => x.attempt.Result == SubmissionStatus.Submitted ||
                        x.attempt.Result == SubmissionStatus.PendingModeration || x.attempt.Result == SubmissionStatus.Approved),
                    Verified = grouped.Count(x => x.backlink != null && x.backlink.Status == BacklinkStatus.Verified)
                })
            .OrderByDescending(x => x.Attempts).ThenBy(x => x.Key).Take(100).ToListAsync(cancellationToken);

        static ReportPerformanceBreakdownDto Performance(string key, int attempts, int successful, int verified) =>
            new(key, attempts, successful, verified, Percentage(successful, attempts), Percentage(verified, attempts));
        static double Percentage(int numerator, int denominator) => denominator == 0
            ? 0 : Math.Round(numerator * 100d / denominator, 2, MidpointRounding.AwayFromZero);

        return new ReportSummaryDto(
            candidates,
            eligible,
            approved,
            submissionCounts?.Queued ?? 0,
            submissionCounts?.Processing ?? 0,
            submissionCounts?.Submitted ?? 0,
            submissionCounts?.Pending ?? 0,
            backlinkCounts?.Verified ?? 0,
            submissionCounts?.Rejected ?? 0,
            submissionCounts?.Failed ?? 0,
            backlinkCounts?.Lost ?? 0,
            backlinkCounts?.Follow ?? 0,
            backlinkCounts?.Nofollow ?? 0,
            backlinkCounts?.Ugc ?? 0,
            backlinkCounts?.Sponsored ?? 0,
            submissionCounts?.Approved ?? 0,
            submissionCounts?.Duplicate ?? 0,
            backlinkCounts?.Pending ?? 0,
            backlinkCounts?.Missing ?? 0,
            backlinkCounts?.Error ?? 0,
            attemptCounts?.Total ?? 0,
            Percentage(attemptCounts?.Successful ?? 0, attemptCounts?.Total ?? 0),
            Percentage(backlinkCounts?.Verified ?? 0, backlinkCounts?.Total ?? 0),
            domainRows.Select(x => Performance(x.Key, x.Attempts, x.Successful, x.Verified)).ToArray(),
            templateRows.Select(x => Performance(x.Key, x.Attempts, x.Successful, x.Verified)).ToArray(),
            identityRows.Select(x => Performance(x.Key, x.Attempts, x.Successful, x.Verified)).ToArray());
    }

    IAsyncEnumerable<ReportRowDto> IReportDataSource.StreamRowsAsync(Guid projectId, Guid? campaignId, ReportKind kind, CancellationToken cancellationToken)
    {
        if (kind == ReportKind.CampaignPerformance)
        {
            var query =
                from campaignOpportunity in dbContext.CampaignOpportunities.AsNoTracking()
                join opportunity in dbContext.Opportunities.AsNoTracking() on campaignOpportunity.OpportunityId equals opportunity.Id
                join campaignTarget in dbContext.CampaignTargets.AsNoTracking() on campaignOpportunity.CampaignId equals campaignTarget.CampaignId
                join target in dbContext.ProjectTargets.AsNoTracking() on campaignTarget.ProjectTargetId equals target.Id
                join submissionValue in dbContext.SubmissionJobs.AsNoTracking() on campaignOpportunity.Id equals submissionValue.CampaignOpportunityId into submissions
                from submission in submissions.DefaultIfEmpty()
                join backlinkValue in dbContext.Backlinks.AsNoTracking() on submission.Id equals backlinkValue.SubmissionJobId into backlinks
                from backlink in backlinks.DefaultIfEmpty()
                where opportunity.ProjectId == projectId && campaignOpportunity.CampaignId == campaignId
                orderby campaignOpportunity.CreatedAt, campaignOpportunity.Id
                select new ReportRowDto(
                    opportunity.SourceUrl,
                    target.Url,
                    backlink == null ? null : backlink.AnchorText,
                    opportunity.Type,
                    submission == null ? null : submission.Status,
                    backlink == null ? null : backlink.Status,
                    backlink == null ? Array.Empty<string>() : backlink.Rel,
                    backlink == null ? null : backlink.HttpStatus,
                    submission == null || (submission.Status != SubmissionStatus.Submitted && submission.Status != SubmissionStatus.PendingModeration) ? null : submission.UpdatedAt,
                    backlink == null ? null : backlink.FirstSeenAt,
                    backlink == null ? null : backlink.LastSeenAt,
                    backlink == null ? null : backlink.LastCheckedAt,
                    submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.Error).FirstOrDefault());
            var ownedQuery =
                from submission in dbContext.SubmissionJobs.AsNoTracking()
                join source in dbContext.SubmissionSources.AsNoTracking() on submission.SubmissionSourceId equals source.Id
                join network in dbContext.OwnedNetworkProfiles.AsNoTracking() on source.OwnedNetworkProfileId equals network.Id
                join backlinkValue in dbContext.Backlinks.AsNoTracking() on submission.Id equals backlinkValue.SubmissionJobId into backlinks
                from backlink in backlinks.DefaultIfEmpty()
                where submission.ProjectId == projectId && submission.CampaignId == campaignId
                orderby submission.CreatedAt, submission.Id
                select new ReportRowDto(
                    source.OriginalUrl,
                    submission.TargetUrl!,
                    backlink == null ? null : backlink.AnchorText,
                    source.OpportunityType,
                    submission.Status,
                    backlink == null ? null : backlink.Status,
                    backlink == null ? Array.Empty<string>() : backlink.Rel,
                    backlink == null ? null : backlink.HttpStatus,
                    submission.Status == SubmissionStatus.Submitted || submission.Status == SubmissionStatus.PendingModeration || submission.Status == SubmissionStatus.Approved ? submission.UpdatedAt : null,
                    backlink == null ? null : backlink.FirstSeenAt,
                    backlink == null ? null : backlink.LastSeenAt,
                    backlink == null ? null : backlink.LastCheckedAt,
                    dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.Error).FirstOrDefault(),
                    source.Domain,
                    network.Name,
                    source.Platform,
                    source.CmsType,
                    dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.ResolvedDisplayName).FirstOrDefault(),
                    (from attempt in dbContext.SubmissionAttempts.AsNoTracking()
                     join template in dbContext.SubmissionTemplates.AsNoTracking() on attempt.TemplateId equals template.Id
                     where attempt.SubmissionJobId == submission.Id
                     orderby attempt.AttemptNumber descending
                     select template.Name).FirstOrDefault(),
                    dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => (ModerationStatus?)x.ModerationStatus).FirstOrDefault(),
                    submission.CreatedAt,
                    dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => (DateTimeOffset?)x.StartedAt).FirstOrDefault(),
                    dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.FinishedAt).FirstOrDefault());
            return StreamCampaignRowsAsync(
                query.AsAsyncEnumerable(),
                ownedQuery.AsAsyncEnumerable(),
                cancellationToken);
        }

        var inventory =
            from backlink in dbContext.Backlinks.AsNoTracking()
            join submissionValue in dbContext.SubmissionJobs.AsNoTracking() on backlink.SubmissionJobId equals submissionValue.Id into submissions
            from submission in submissions.DefaultIfEmpty()
            join campaignOpportunityValue in dbContext.CampaignOpportunities.AsNoTracking() on submission.CampaignOpportunityId equals campaignOpportunityValue.Id into campaignOpportunities
            from campaignOpportunity in campaignOpportunities.DefaultIfEmpty()
            join opportunityValue in dbContext.Opportunities.AsNoTracking() on campaignOpportunity.OpportunityId equals opportunityValue.Id into opportunities
            from opportunity in opportunities.DefaultIfEmpty()
            join sourceValue in dbContext.SubmissionSources.AsNoTracking() on submission.SubmissionSourceId equals sourceValue.Id into sources
            from source in sources.DefaultIfEmpty()
            join networkValue in dbContext.OwnedNetworkProfiles.AsNoTracking() on source.OwnedNetworkProfileId equals networkValue.Id into networks
            from network in networks.DefaultIfEmpty()
            where backlink.ProjectId == projectId && (campaignId == null || backlink.CampaignId == campaignId)
            orderby backlink.CreatedAt, backlink.Id
            select new ReportRowDto(
                backlink.SourceUrl,
                backlink.TargetUrl,
                backlink.AnchorText,
                opportunity == null ? null : opportunity.Type,
                submission == null ? null : submission.Status,
                backlink.Status,
                backlink.Rel,
                backlink.HttpStatus,
                submission == null || (submission.Status != SubmissionStatus.Submitted && submission.Status != SubmissionStatus.PendingModeration) ? null : submission.UpdatedAt,
                backlink.FirstSeenAt,
                backlink.LastSeenAt,
                backlink.LastCheckedAt,
                submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.Error).FirstOrDefault(),
                source == null ? backlink.Domain : source.Domain,
                network == null ? null : network.Name,
                source == null ? null : source.Platform,
                source == null ? null : source.CmsType,
                submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.ResolvedDisplayName).FirstOrDefault(),
                submission == null ? null : (from attempt in dbContext.SubmissionAttempts.AsNoTracking()
                                             join template in dbContext.SubmissionTemplates.AsNoTracking() on attempt.TemplateId equals template.Id
                                             where attempt.SubmissionJobId == submission.Id
                                             orderby attempt.AttemptNumber descending
                                             select template.Name).FirstOrDefault(),
                submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => (ModerationStatus?)x.ModerationStatus).FirstOrDefault(),
                submission == null ? null : submission.CreatedAt,
                submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => (DateTimeOffset?)x.StartedAt).FirstOrDefault(),
                submission == null ? null : dbContext.SubmissionAttempts.AsNoTracking().Where(x => x.SubmissionJobId == submission.Id).OrderByDescending(x => x.AttemptNumber).Select(x => x.FinishedAt).FirstOrDefault());
        return inventory.AsAsyncEnumerable();
    }

    private static async IAsyncEnumerable<ReportRowDto> StreamCampaignRowsAsync(
        IAsyncEnumerable<ReportRowDto> opportunityRows,
        IAsyncEnumerable<ReportRowDto> ownedNetworkRows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in opportunityRows.WithCancellation(cancellationToken))
        {
            yield return row;
        }

        await foreach (var row in ownedNetworkRows.WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

}
