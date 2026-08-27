using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public interface IStudioUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IProjectRepository
{
    void Add(Project project);
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListAsync(PageCursor? cursor, int take, CancellationToken cancellationToken);
    void AddTarget(ProjectTarget target);
    Task<IReadOnlyList<ProjectTarget>> ListTargetsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectTarget>> ListEnabledTargetsAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ProjectTarget?> GetTargetAsync(Guid id, CancellationToken cancellationToken);
}

public interface IOwnedNetworkRepository
{
    void Add(OwnedNetworkProfile profile);
    void AddDomain(OwnedNetworkDomain domain);
    Task<OwnedNetworkProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<OwnedNetworkProfile?> FindByNameAsync(Guid projectId, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<OwnedNetworkProfile>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<OwnedNetworkDomain>> ListDomainsAsync(Guid ownedNetworkProfileId, bool tracked, CancellationToken cancellationToken);
    void RemoveDomains(IEnumerable<OwnedNetworkDomain> domains);
}

public sealed record SubmissionSourceImportItem(
    Guid SourceImportId,
    string OriginalUrl,
    string NormalizedUrl,
    string Domain,
    string Host,
    SourcePlatform Platform,
    CmsType CmsType,
    OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    string? Tag,
    bool Enabled);

public sealed record SubmissionSourceImportPersistenceResult(int Accepted, int Duplicates);

public interface ISubmissionSourceRepository
{
    void Add(SubmissionSource source) => throw new NotSupportedException();
    void AddImport(SubmissionSourceImport sourceImport);
    Task StageImportChunkAsync(Guid importId, int sequence, ReadOnlyMemory<byte> content, DateTimeOffset now, CancellationToken cancellationToken);
    IAsyncEnumerable<ReadOnlyMemory<byte>> StreamImportChunksAsync(Guid importId, CancellationToken cancellationToken);
    Task<SubmissionSourceImport?> GetImportAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<SubmissionSourceImport?> FindImportByIdempotencyAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken);
    Task<SubmissionSourceImportPersistenceResult> ImportBatchAsync(Guid projectId, Guid ownedNetworkProfileId, IReadOnlyList<SubmissionSourceImportItem> items, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> CountByImportAsync(Guid importId, CancellationToken cancellationToken);
    Task DeleteImportChunksAsync(Guid importId, CancellationToken cancellationToken);
    Task<SubmissionSource?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<SubmissionSource?> FindByNormalizedUrlAsync(Guid projectId, string normalizedUrl, bool tracked,
        CancellationToken cancellationToken) => Task.FromResult<SubmissionSource?>(null);
    Task<IReadOnlyList<SubmissionSource>> ListByImportAsync(Guid sourceImportId, int take,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionSource>>([]);
    Task<IReadOnlyList<SubmissionSource>> ListAsync(Guid projectId, SubmissionSourceFilter filter, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionSource>> ListValidationCandidatesAsync(Guid projectId, Guid? ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public sealed record CandidateForAnalysis(CandidatePage Page, string Domain);
public sealed record CandidateImportItem(string OriginalUrl, string NormalizedUrl, string Domain);
public sealed record CandidateImportPersistenceResult(int Accepted, int Duplicates);

public interface ICandidateRepository
{
    Task<CandidateImportPersistenceResult> ImportAsync(Guid projectId, IReadOnlyList<CandidateImportItem> items, DateTimeOffset now, CancellationToken cancellationToken);
    Task<CandidateDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CandidateDto>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<CandidateForAnalysis>> GetUnanalyzedAsync(Guid projectId, int take, CancellationToken cancellationToken);
    void ApplyAnalysis(CandidatePage page, SiteAnalysisResult analysis, bool existingTargetLink);
    void MarkBlocked(CandidatePage page, string reason, DateTimeOffset analyzedAt);
}

public interface IPolicyRepository
{
    Task<PolicyDefinition?> GetAsync(Guid projectId, bool tracked, CancellationToken cancellationToken);
    void Add(PolicyDefinition policy);
    Task<IReadOnlyList<BlocklistEntry>> ListBlocklistAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<BlocklistEntry>> ListEnabledBlocklistAsync(Guid projectId, CancellationToken cancellationToken);
    Task<BlocklistEntry?> FindBlocklistAsync(Guid projectId, BlocklistMatchType matchType, string value, CancellationToken cancellationToken);
    void Add(BlocklistEntry entry);
}

public interface IOpportunityRepository
{
    Task<bool> ExistsForCandidateAsync(Guid candidatePageId, CancellationToken cancellationToken);
    void Add(Opportunity opportunity);
    Task<OpportunityDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpportunityDto>> ListAsync(Guid projectId, int? minimumQuality, int? maximumRisk, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<OpportunitySummaryDto> SummaryAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Opportunity>> GetTrackedAsync(Guid projectId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public sealed record SubmissionWorkItem(SubmissionJob Submission, Campaign Campaign, CampaignOpportunity CampaignOpportunity, Opportunity Opportunity, ProjectTarget Target);
public sealed record OwnedNetworkSubmissionWorkItem(SubmissionJob Submission, Campaign Campaign,
    OwnedNetworkCampaignConfiguration Configuration, SubmissionSource Source);

public interface IOwnedNetworkCampaignRepository
{
    void Add(OwnedNetworkCampaignConfiguration configuration);
    Task<OwnedNetworkCampaignConfiguration?> GetAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> ListExistingSubmissionSourceIdsAsync(Guid campaignId,
        IReadOnlyCollection<Guid> submissionSourceIds, string targetUrl, BacklinkPlacementMethod placementType,
        CancellationToken cancellationToken);
    Task<OwnedNetworkSubmissionWorkItem?> GetWorkItemAsync(Guid persistentJobId, CancellationToken cancellationToken);
    Task<int> CountSuccessfulActionsAsync(Guid projectId, Guid? campaignId, string? domain, DateTimeOffset since, CancellationToken cancellationToken);
}

public interface ISubmissionRepository
{
    void AddCampaign(Campaign campaign);
    void AddCampaignTarget(CampaignTarget target);
    void AddCampaignOpportunity(CampaignOpportunity opportunity);
    void AddSubmission(SubmissionJob submission);
    void AddAttempt(SubmissionAttempt attempt);
    Task<Campaign?> GetCampaignAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<Campaign>> ListCampaignsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<CampaignOpportunity>> ListCampaignOpportunitiesAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionJob>> ListCampaignSubmissionJobsAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersistentJob>> ListCampaignJobsAsync(Guid campaignId, bool tracked, CancellationToken cancellationToken);
    Task<int> PauseCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> ResumeCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> StopCampaignJobsAsync(Guid campaignId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> SubmissionExistsAsync(Guid campaignOpportunityId, CancellationToken cancellationToken);
    Task<bool> HasSuccessfulSubmissionAsync(Guid projectId, Guid opportunityId, Guid excludingSubmissionId, CancellationToken cancellationToken);
    Task<SubmissionWorkItem?> GetWorkItemAsync(Guid persistentJobId, CancellationToken cancellationToken);
    Task<int> CountSuccessfulActionsAsync(Guid projectId, Guid? campaignId, string? domain, DateTimeOffset since, CancellationToken cancellationToken);
    Task<SubmissionJob?> GetSubmissionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionJob>> ListSubmissionsAsync(Guid campaignId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionAttempt>> ListAttemptsAsync(Guid submissionJobId, int take, CancellationToken cancellationToken);
}

public interface IBacklinkRepository
{
    void Add(Backlink backlink);
    void AddCheck(VerificationCheck check);
    Task<Backlink?> FindBySubmissionAsync(Guid submissionJobId, CancellationToken cancellationToken);
    Task<Backlink?> FindBySourceTargetAsync(Guid projectId, string normalizedSourceUrl, string normalizedTargetUrl, CancellationToken cancellationToken);
    Task<Backlink?> GetBacklinkAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<Backlink>> ListAsync(Guid projectId, BacklinkStatus? status, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<VerificationCheck>> ListHistoryAsync(Guid backlinkId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public interface IReportRepository
{
    void Add(Report report);
    Task<Report?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<Report>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public interface IReportDataSource
{
    Task<ReportSummaryDto> GetSummaryAsync(Guid projectId, Guid? campaignId, ReportKind kind, CancellationToken cancellationToken);
    IAsyncEnumerable<ReportRowDto> StreamRowsAsync(Guid projectId, Guid? campaignId, ReportKind kind, CancellationToken cancellationToken);
}

public sealed record ReportArtifactMetadata(string ArtifactName, string ContentType, long ByteLength, string Sha256);

public interface IReportArtifactWriter : IAsyncDisposable
{
    Stream Stream { get; }
    Task<ReportArtifactMetadata> CommitAsync(string artifactName, string contentType, CancellationToken cancellationToken);
}

public interface IReportArtifactStore
{
    Task<IReportArtifactWriter> BeginWriteAsync(Guid reportId, ReportFormat format, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(Guid reportId, ReportFormat format, CancellationToken cancellationToken);
}

public interface IDiscoveryRepository
{
    void Add(DiscoveryQuery query);
    void Add(DiscoveryRun run);
    Task<(DiscoveryRun Run, DiscoveryQuery Query)?> GetTrackedAsync(Guid runId, CancellationToken cancellationToken);
    Task<DiscoveryRunDto?> GetAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DiscoveryRunDto>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public interface IAuditSink
{
    void Append(AuditEvent auditEvent);
}

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken);
    void Add(IdempotencyRecord record);
}

public interface IJobQueue
{
    Task<PersistentJob> EnqueueAsync(PersistentJob job, CancellationToken cancellationToken);
    Task<PersistentJob?> FindByIdempotencyAsync(Guid projectId, JobType jobType, string idempotencyKey, CancellationToken cancellationToken);
    Task<JobCorrelationSummary> GetCorrelationSummaryAsync(Guid projectId, JobType jobType, string correlationId,
        CancellationToken cancellationToken) => Task.FromResult(new JobCorrelationSummary(0, 0));
    Task<PersistentJob?> ClaimAsync(string workerId, TimeSpan lease, JobClaimLimits limits, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkRunningAsync(Guid jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkSucceededAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid jobId, string workerId, string errorMessage, JobFailureDecision failure, DateTimeOffset now, TimeSpan retryDelay, CancellationToken cancellationToken);
    Task<JobLeaseRenewal> RenewLeaseAsync(Guid jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken);
    Task AcknowledgePauseAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PersistentJob> PauseAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PersistentJob> ResumeAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PersistentJob> RedriveAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PersistentJob?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersistentJob>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public sealed record JobCorrelationSummary(int Active, int Failed);

public interface IWorkerRegistry
{
    Task RegisterAsync(WorkerRegistration registration, CancellationToken cancellationToken);
    Task HeartbeatAsync(string workerId, int activeJobs, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkStoppedAsync(string workerId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDomainRateLimiter
{
    Task WaitAsync(Guid projectId, Guid? campaignId, string domain, CancellationToken cancellationToken, int? minimumDelayMilliseconds = null);
}

public interface IJobExecutor
{
    JobType JobType { get; }
    Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken);
}
