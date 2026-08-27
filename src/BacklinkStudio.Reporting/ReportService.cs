using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Reporting;

public sealed class ReportService(
    IProjectRepository projects,
    ISubmissionRepository submissions,
    IReportRepository reports,
    IReportArtifactStore artifactStore,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IReportService
{
    public async Task<ReportAcceptedDto> GenerateAsync(GenerateReportCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (command.Kind == ReportKind.CampaignPerformance && command.CampaignId is null)
        {
            throw new ValidationException("Campaign performance reports require campaignId.");
        }
        if (command.CampaignId is not null)
        {
            var campaign = await submissions.GetCampaignAsync(command.CampaignId.Value, false, cancellationToken)
                ?? throw new ResourceNotFoundException("Campaign", command.CampaignId.Value);
            if (campaign.ProjectId != command.ProjectId)
            {
                throw new ValidationException("Campaign does not belong to the requested project.");
            }
        }

        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "report_generate");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<ReportAcceptedDto>(existing, requestHash);

        var now = timeProvider.GetUtcNow();
        var report = new Report(command.ProjectId, command.CampaignId, command.Kind, command.Format, now);
        var queueKey = Idempotency.HashRequest(new { actor.ActorType, actor.ActorId, Key = key });
        var job = new PersistentJob(JobType.Report, command.ProjectId, command.CampaignId, JsonSerializer.Serialize(new ReportJobPayload(1, report.Id)), 0, now, 3, actor.RequestId, queueKey, now);
        report.AttachJob(job.Id, now);
        reports.Add(report);
        await jobs.EnqueueAsync(job, cancellationToken);

        var result = new ReportAcceptedDto(report.Id, job.Id, "queued");
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "report", report.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "report.enqueue", command.ProjectId, command.CampaignId, job.Id, actor.RequestId, $"reportId={report.Id};kind={command.Kind};format={command.Format}", "queued", actor.SourceAddress, now));
        BacklinkStudioTelemetry.JobsQueued.Add(1, new KeyValuePair<string, object?>("job.type", JobType.Report.ToString()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ReportDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await reports.GetAsync(id, false, cancellationToken))?.ToDto();

    public async Task<PageResult<ReportDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await reports.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id))
            : null;
        return new(values.Select(x => x.ToDto()).ToArray(), next);
    }

    public async Task<ReportDownload?> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await reports.GetAsync(id, false, cancellationToken);
        if (report is null) return null;
        if (report.Status != ReportStatus.Completed || report.ArtifactName is null || report.ContentType is null || report.ByteLength is null || report.Sha256 is null)
        {
            throw new ConflictException("Report artifact is not ready.");
        }

        var stream = await artifactStore.OpenReadAsync(report.Id, report.Format, cancellationToken)
            ?? throw new ConflictException("Report artifact is unavailable.");
        return new ReportDownload(stream, report.ContentType, report.ArtifactName, report.ByteLength.Value, report.Sha256);
    }
}

public sealed record ReportJobPayload(int Version, Guid ReportId);
