using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Verification;

public sealed class VerificationJobExecutor(
    IBacklinkRepository backlinks,
    IBacklinkVerifier verifier,
    IControlledBrowserBacklinkVerifier browserVerifier,
    ISubmissionSourceRepository sources,
    IOwnedNetworkExecutionAuthorizer authorizer,
    IDomainRateLimiter rateLimiter,
    IBacklinkWorkflowRepository workflows,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork) : IJobExecutor
{
    public JobType JobType => JobType.Verification;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<VerificationJobPayload>(job.Payload);
        if (payload?.Version != 1) throw new ValidationException("Verification job payload is invalid or unsupported.");
        var backlink = await backlinks.GetBacklinkAsync(payload.BacklinkId, true, cancellationToken) ?? throw new ValidationException("Backlink verification target is missing.");
        if (backlink.ProjectId != job.ProjectId || !string.Equals(backlink.Domain, job.Domain, StringComparison.Ordinal)) throw new ValidationException("Verification job scope does not match its backlink.");

        await rateLimiter.WaitAsync(job.ProjectId, job.CampaignId, backlink.Domain, cancellationToken);
        var sourceUri = new Uri(backlink.NormalizedSourceUrl);
        var result = await verifier.VerifyAsync(sourceUri, backlink.NormalizedTargetUrl, cancellationToken);
        if (ShouldTryBrowser(result) && backlink.SubmissionSourceId is { } browserSourceId)
        {
            var source = await sources.GetAsync(browserSourceId, false, cancellationToken);
            var authorization = source is null
                ? null
                : await authorizer.AuthorizeSourceAsync(job.ProjectId, source, cancellationToken);
            if (source is not null && authorization is { Allowed: true } &&
                string.Equals(source.Host, sourceUri.IdnHost, StringComparison.OrdinalIgnoreCase))
                result = await browserVerifier.VerifyAsync(sourceUri, source.Host, backlink.NormalizedTargetUrl,
                    cancellationToken);
        }
        var priorStatus = backlink.Status;
        backlink.ApplyVerification(result.Found, result.HttpStatus, result.Anchor, result.Rel, result.CanonicalUrl, result.Error, result.CheckedAt);
        backlinks.AddCheck(new VerificationCheck(backlink.Id, result.CheckedAt, result.Found, result.HttpStatus, result.Anchor, result.Rel, result.Error, result.Duration));
        if (backlink.SubmissionSourceId is { } sourceId &&
            Guid.TryParse(job.CorrelationId, out var workflowId))
        {
            await workflows.ApplyVerificationAsync(workflowId, sourceId,
                backlink.Status == BacklinkStatus.Verified,
                backlink.Status == BacklinkStatus.Verified ? null : result.Error ?? "The submitted backlink was not found.",
                result.CheckedAt, cancellationToken);
        }
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "verification.complete", backlink.ProjectId, backlink.CampaignId, job.Id, job.CorrelationId, $"backlinkId={backlink.Id};found={result.Found};httpStatus={result.HttpStatus};status={backlink.Status}", result.Error is null ? "succeeded" : "error", null, result.CheckedAt));
        var domainTag = new KeyValuePair<string, object?>("server.address", backlink.Domain);
        BacklinkStudioTelemetry.VerificationChecks.Add(1, domainTag);
        BacklinkStudioTelemetry.VerificationDuration.Record(result.Duration.TotalSeconds, domainTag);
        if (result.Found) BacklinkStudioTelemetry.VerificationFound.Add(1, domainTag);
        if (backlink.Status == BacklinkStatus.Verified && priorStatus != BacklinkStatus.Verified)
            BacklinkStudioTelemetry.BacklinksVerified.Add(1, domainTag);
        if (backlink.Status == BacklinkStatus.Missing && priorStatus != BacklinkStatus.Missing)
            BacklinkStudioTelemetry.BacklinksMissing.Add(1, domainTag);
        if (priorStatus != BacklinkStatus.Lost && backlink.Status == BacklinkStatus.Lost) BacklinkStudioTelemetry.BacklinksLost.Add(1, domainTag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static bool ShouldTryBrowser(BacklinkVerificationResult result) => !result.Found &&
        (result.Error == "Response exceeds the configured verification size limit." ||
         result.Error is null && result.HttpStatus is >= 200 and < 300);
}
