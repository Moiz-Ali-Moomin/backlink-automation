using System.Diagnostics;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class SubmissionSourceValidationJobExecutor(
    ISubmissionSourceRepository sources,
    IWordPressSiteProfileRepository wordpressProfiles,
    ISubmissionSourceInspector inspector,
    IControlledBrowserValidationAdapter browserInspector,
    IOwnedNetworkExecutionAuthorizer ownedNetworkAuthorizer,
    IBacklinkWorkflowRepository workflows,
    IJobQueue jobs,
    IDomainRateLimiter rateLimiter,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    private const int CoordinatorBatchSize = 500;
    public JobType JobType => JobType.SubmissionSourceValidation;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SubmissionSourceValidationJobPayload>(job.Payload);
        if (payload?.Version != 1 || payload.Remaining < 1)
            throw new ValidationException("Submission source validation payload is invalid or unsupported.");
        if (payload.SubmissionSourceId is null)
            await CoordinateAsync(job, payload, workerId, cancellationToken);
        else
            await ValidateSourceAsync(job, payload, workerId, cancellationToken);
    }

    private async Task CoordinateAsync(PersistentJob job, SubmissionSourceValidationJobPayload payload, string workerId, CancellationToken cancellationToken)
    {
        PageCursor? cursor = payload.CursorCreatedAt is not null && payload.CursorId is not null
            ? new PageCursor(payload.CursorCreatedAt.Value, payload.CursorId.Value)
            : null;
        var take = Math.Min(CoordinatorBatchSize, payload.Remaining);
        var rows = await sources.ListValidationCandidatesAsync(job.ProjectId, payload.OwnedNetworkProfileId, cursor, take, cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var source in rows)
        {
            source.QueueValidation(now);
            var child = new PersistentJob(JobType.SubmissionSourceValidation, job.ProjectId, null,
                JsonSerializer.Serialize(payload with
                {
                    SubmissionSourceId = source.Id,
                    CursorCreatedAt = null,
                    CursorId = null,
                    Remaining = 1
                }), 0, now, 3, job.CorrelationId,
                $"submission-source-validation:{payload.RunId}:source:{source.Id}", now, source.Domain);
            await jobs.EnqueueAsync(child, cancellationToken);
        }
        var remaining = payload.Remaining - rows.Count;
        if (rows.Count == take && remaining > 0)
        {
            var last = rows[^1];
            var next = new PersistentJob(JobType.SubmissionSourceValidation, job.ProjectId, null,
                JsonSerializer.Serialize(payload with
                {
                    SubmissionSourceId = null,
                    CursorCreatedAt = last.CreatedAt,
                    CursorId = last.Id,
                    Remaining = remaining
                }), 1, now, 3, job.CorrelationId,
                $"submission-source-validation:{payload.RunId}:coordinator:{last.Id}", now);
            await jobs.EnqueueAsync(next, cancellationToken);
        }
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission_sources.validation_dispatched",
            job.ProjectId, null, job.Id, job.CorrelationId,
            $"runId={payload.RunId};jobsQueued={rows.Count};remaining={remaining}", "succeeded", null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateSourceAsync(PersistentJob job, SubmissionSourceValidationJobPayload payload, string workerId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowId = Guid.TryParse(job.CorrelationId, out var parsedWorkflowId)
            ? parsedWorkflowId
            : (Guid?)null;
        var source = await sources.GetAsync(payload.SubmissionSourceId!.Value, true, cancellationToken)
            ?? throw new ValidationException("Submission source is missing.");
        if (source.ProjectId != job.ProjectId || !source.Enabled)
            throw new PolicyRejectedException("Submission source is disabled or belongs to another project.");
        var ownership = await ownedNetworkAuthorizer.AuthorizeSourceAsync(job.ProjectId, source,
            cancellationToken);
        await SetWorkflowStatusAsync(workflowId, source.Id, BacklinkWorkflowSourceStatus.Checking, null,
            timeProvider.GetUtcNow(), cancellationToken);
        if (!ownership.Allowed || ownership.Profile is null)
        {
            var deniedAt = timeProvider.GetUtcNow();
            await SetWorkflowStatusAsync(workflowId, source.Id, BacklinkWorkflowSourceStatus.NotAuthorized,
                "The source is not authorized by the persisted project/network policy.", deniedAt,
                cancellationToken);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null,
                "submission_source.authorization_denied", job.ProjectId, null, job.Id, job.CorrelationId,
                $"submissionSourceId={source.Id};reason={ownership.Reason}", "not_authorized", null, deniedAt));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }
        source.AssociateWithOwnedNetwork(ownership.Profile.Id, ownership.Profile.OwnershipStatus, true,
            timeProvider.GetUtcNow());

        try
        {
            await rateLimiter.WaitAsync(job.ProjectId, null, source.Domain, cancellationToken);
            var result = await inspector.InspectAsync(new Uri(source.NormalizedUrl, UriKind.Absolute), ownership.Allowed, cancellationToken);
            var wordpressProfile = await wordpressProfiles.FindForSourceAsync(ownership.Profile.Id, source.Host,
                cancellationToken);
            if (ownership.Allowed && result.RequiresBrowser &&
                !result.RequiresAuthentication && !result.RequiresManualAction)
            {
                result = await browserInspector.InspectAsync(source, true, cancellationToken);
            }
            if (result.SupportsWordPressComment)
                result = result with
                {
                    AdapterName = nameof(OwnedWordPressCommentAdapter),
                    SupportsOwnedWordPressApi = wordpressProfile?.SubmissionMode is
                        WordPressSubmissionMode.DirectApi or WordPressSubmissionMode.AuthenticatedIntegration
                };
            var completedAt = timeProvider.GetUtcNow();
            source.ApplyValidation(result, completedAt);
            var workflowStatus = ClassifyWorkflow(result, ownership.Allowed);
            await SetWorkflowStatusAsync(workflowId, source.Id, workflowStatus.Status, workflowStatus.Reason,
                completedAt, cancellationToken);
            var cmsTag = new KeyValuePair<string, object?>("source.cms", result.CmsType.ToString());
            BacklinkStudioTelemetry.BacklinkSourcesValidated.Add(1, cmsTag);
            if (result.CmsType == CmsType.WordPress) BacklinkStudioTelemetry.BacklinkSourcesWordPress.Add(1);
            if (result.SupportsWordPressComment) BacklinkStudioTelemetry.BacklinkSourcesCommentCapable.Add(1);
            BacklinkStudioTelemetry.SourceValidationDuration.Record(stopwatch.Elapsed.TotalSeconds, cmsTag);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission_source.validation_complete",
                job.ProjectId, null, job.Id, job.CorrelationId,
                $"submissionSourceId={source.Id};status={result.ValidationStatus};cms={result.CmsType};opportunity={result.OpportunityType};compatible={result.TechnicalCompatibility};authorized={ownership.Allowed};adapter={result.AdapterName}",
                result.ValidationStatus == SubmissionSourceValidationStatus.Valid ? "succeeded" : "invalid", null, completedAt));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            var failedAt = timeProvider.GetUtcNow();
            source.ApplyValidation(Error(exception.Message, source.NormalizedUrl), failedAt);
            await SetWorkflowStatusAsync(workflowId, source.Id, BacklinkWorkflowSourceStatus.Unsupported,
                exception.Message, failedAt, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when ((exception is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            var failedAt = timeProvider.GetUtcNow();
            source.ApplyValidation(Error("Source validation request failed.", source.NormalizedUrl, SubmissionSourceValidationStatus.Error), failedAt);
            await SetWorkflowStatusAsync(workflowId, source.Id, BacklinkWorkflowSourceStatus.Failed,
                "Source validation request failed.", failedAt, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private Task SetWorkflowStatusAsync(Guid? workflowId, Guid submissionSourceId,
        BacklinkWorkflowSourceStatus status, string? reason, DateTimeOffset now,
        CancellationToken cancellationToken) => workflowId is { } id
        ? workflows.SetSourceStatusAsync(id, submissionSourceId, status, reason, now, cancellationToken)
        : Task.CompletedTask;

    private static SubmissionSourceValidation Error(string reason, string finalUrl, SubmissionSourceValidationStatus status = SubmissionSourceValidationStatus.Invalid) =>
        new(SourcePlatform.Unknown, CmsType.Unknown, OpportunityType.Unknown, null, TechnicalCompatibility.Unknown,
            status, reason, reason, false, false, false, false, false, false, null, null, null, null, finalUrl,
            null, null, null, null, [finalUrl], false, null, null, null, null, null, [], false, false, null);

    private static (BacklinkWorkflowSourceStatus Status, string? Reason) ClassifyWorkflow(
        SubmissionSourceValidation result, bool authorized)
    {
        if (!authorized) return (BacklinkWorkflowSourceStatus.NotAuthorized, "The source is not authorized for automation.");
        if (result.HttpStatus == 403) return (BacklinkWorkflowSourceStatus.Rejected, result.ValidationReason);
        if (result.ValidationStatus == SubmissionSourceValidationStatus.Error)
            return (BacklinkWorkflowSourceStatus.Failed, result.ValidationReason);
        if (result.RequiresAuthentication)
            return (BacklinkWorkflowSourceStatus.AuthenticationRequired, result.ValidationReason);
        if (result.ValidationReason?.Contains("comments are closed", StringComparison.OrdinalIgnoreCase) == true)
            return (BacklinkWorkflowSourceStatus.CommentsClosed, result.ValidationReason);
        if (result.RequiresManualAction && result.ValidationReason?.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true)
            return (BacklinkWorkflowSourceStatus.Unsupported, result.ValidationReason);
        if (result.TechnicalCompatibility is TechnicalCompatibility.Compatible or TechnicalCompatibility.FallbackCandidate &&
            result.ValidationStatus == SubmissionSourceValidationStatus.Valid)
            return (BacklinkWorkflowSourceStatus.Queued, null);
        return (BacklinkWorkflowSourceStatus.Unsupported, result.ValidationReason);
    }
}
