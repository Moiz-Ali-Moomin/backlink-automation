using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Reporting;

public sealed class ReportJobExecutor(
    IReportRepository reports,
    IReportDataSource dataSource,
    IReportArtifactStore artifactStore,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    public JobType JobType => JobType.Report;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ReportJobPayload>(job.Payload);
        if (payload?.Version != 1) throw new ValidationException("Report job payload is invalid or unsupported.");
        var report = await reports.GetAsync(payload.ReportId, true, cancellationToken)
            ?? throw new ValidationException("Report job target is missing.");
        if (report.ProjectId != job.ProjectId || report.CampaignId != job.CampaignId || report.JobId != job.Id)
        {
            throw new ValidationException("Report job scope does not match its report.");
        }

        var startedAt = timeProvider.GetUtcNow();
        report.Start(startedAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var summary = await dataSource.GetSummaryAsync(report.ProjectId, report.CampaignId, report.Kind, cancellationToken);
            var rows = dataSource.StreamRowsAsync(report.ProjectId, report.CampaignId, report.Kind, cancellationToken);
            await using var writer = await artifactStore.BeginWriteAsync(report.Id, report.Format, cancellationToken);
            var rendering = await ReportDocumentRenderer.RenderAsync(report, summary, rows, writer.Stream, cancellationToken);
            var artifact = await writer.CommitAsync(rendering.ArtifactName, rendering.ContentType, cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            report.Complete(artifact.ArtifactName, artifact.ContentType, artifact.ByteLength, artifact.Sha256, rendering.RowCount, completedAt);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "report.complete", report.ProjectId, report.CampaignId, job.Id, job.CorrelationId, $"reportId={report.Id};kind={report.Kind};format={report.Format};rows={rendering.RowCount};bytes={artifact.ByteLength}", "succeeded", null, completedAt));
            BacklinkStudioTelemetry.ReportsGenerated.Add(1, new KeyValuePair<string, object?>("report.format", report.Format.ToString()));
            BacklinkStudioTelemetry.ReportRows.Add(rendering.RowCount, new KeyValuePair<string, object?>("report.kind", report.Kind.ToString()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            if (report.Status == ReportStatus.Generating)
            {
                var failedAt = timeProvider.GetUtcNow();
                report.Fail("Report generation failed.", failedAt);
                audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "report.complete", report.ProjectId, report.CampaignId, job.Id, job.CorrelationId, $"reportId={report.Id};kind={report.Kind};format={report.Format}", "failed", null, failedAt));
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
    }
}
