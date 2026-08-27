using System.Security.Cryptography;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class SubmissionSourceService(
    IProjectRepository projects,
    IOwnedNetworkRepository networks,
    ISubmissionSourceRepository sources,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ISubmissionSourceService
{
    private const int ChunkSize = 64 * 1_024;
    private const long MaximumImportBytes = 1L * 1_024 * 1_024 * 1_024;

    public async Task<SubmissionSourceImportAcceptedDto> ImportAsync(ImportSubmissionSourcesCommand command, Stream content, ActorContext actor, CancellationToken cancellationToken)
    {
        if (!content.CanRead) throw new ValidationException("Import content must be readable.");
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "submission_sources_import");
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            var existingHash = await ComputeHashAsync(content, cancellationToken);
            var existingRequestHash = RequestHash(command, existingHash);
            return Idempotency.ReadExisting<SubmissionSourceImportAcceptedDto>(existing, existingRequestHash);
        }
        if (await sources.FindImportByIdempotencyAsync(command.ProjectId, key, cancellationToken) is not null)
            throw new ConflictException("This source import idempotency key is already being processed.");
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var network = await networks.GetAsync(command.OwnedNetworkProfileId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", command.OwnedNetworkProfileId);
        if (network.ProjectId != command.ProjectId) throw new ValidationException("The owned network must belong to the import project.");

        var now = timeProvider.GetUtcNow();
        var sourceImport = new SubmissionSourceImport(command.ProjectId, command.OwnedNetworkProfileId, command.Format,
            command.FileName, command.Tag, key, now);
        sources.AddImport(sourceImport);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (byteLength, sha256) = await StageAsync(sourceImport.Id, content, cancellationToken);
        var sealedAt = timeProvider.GetUtcNow();
        sourceImport.SealStaging(byteLength, sha256, sealedAt);
        var persistent = new PersistentJob(JobType.SubmissionSourceImport, command.ProjectId, null,
            JsonSerializer.Serialize(new SubmissionSourceImportJobPayload(1, sourceImport.Id)), 0, sealedAt, 3,
            actor.RequestId, $"submission-source-import:{sourceImport.Id}", sealedAt);
        await jobs.EnqueueAsync(persistent, cancellationToken);
        sourceImport.Queue(persistent.Id, sealedAt);
        var result = new SubmissionSourceImportAcceptedDto(sourceImport.Id, persistent.Id, "queued");
        var requestHash = RequestHash(command, sha256);
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "submission_source_import", sourceImport.Id,
            JsonSerializer.Serialize(result), sealedAt));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "submission_sources.import_queued",
            command.ProjectId, null, persistent.Id, actor.RequestId,
            $"importId={sourceImport.Id};networkId={network.Id};format={command.Format};bytes={byteLength};sha256={sha256}",
            "queued", actor.SourceAddress, sealedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionSourceImportDto?> GetImportAsync(Guid id, CancellationToken cancellationToken) =>
        (await sources.GetImportAsync(id, false, cancellationToken))?.ToDto();

    public async Task<SubmissionSourceDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await sources.GetAsync(id, false, cancellationToken))?.ToDto();

    public async Task<PageResult<SubmissionSourceDto>> ListAsync(Guid projectId, SubmissionSourceFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var boundedFilter = filter with
        {
            Domain = NormalizeOptional(filter.Domain, 253),
            AdapterName = NormalizeOptional(filter.AdapterName, 100),
            Tag = NormalizeOptional(filter.Tag, 100)
        };
        var rows = await sources.ListAsync(projectId, boundedFilter, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id))
            : null;
        return new(values.Select(OwnedNetworkMapping.ToDto).ToArray(), next);
    }

    public async Task<SubmissionSourceValidationAcceptedDto> ValidateAsync(ValidateSubmissionSourcesCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        if (command.MaximumSources is < 1 or > 1_000_000)
            throw new ValidationException("Maximum validation sources must be between 1 and 1000000.");
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "submission_sources_validate");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<SubmissionSourceValidationAcceptedDto>(existing, hash);
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (command.OwnedNetworkProfileId is { } networkId)
        {
            var network = await networks.GetAsync(networkId, false, cancellationToken)
                ?? throw new ResourceNotFoundException("OwnedNetworkProfile", networkId);
            if (network.ProjectId != command.ProjectId) throw new ValidationException("The owned network must belong to the validation project.");
        }

        var now = timeProvider.GetUtcNow();
        var runId = Guid.CreateVersion7(now);
        var persistent = new PersistentJob(JobType.SubmissionSourceValidation, command.ProjectId, null,
            JsonSerializer.Serialize(new SubmissionSourceValidationJobPayload(1, runId, command.OwnedNetworkProfileId,
                null, null, null, command.MaximumSources)), 0, now, 3, actor.RequestId,
            $"submission-source-validation:{runId}:coordinator:start", now);
        await jobs.EnqueueAsync(persistent, cancellationToken);
        var result = new SubmissionSourceValidationAcceptedDto(persistent.Id, "queued");
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "submission_source_validation", persistent.Id,
            JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "submission_sources.validation_queued",
            command.ProjectId, null, persistent.Id, actor.RequestId,
            $"runId={runId};networkId={command.OwnedNetworkProfileId};maximumSources={command.MaximumSources}",
            "queued", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<(long ByteLength, string Sha256)> StageAsync(Guid importId, Stream content, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ChunkSize];
        long total = 0;
        var sequence = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumImportBytes) throw new ValidationException("Source import must not exceed 1 GiB.");
            hash.AppendData(buffer.AsSpan(0, read));
            await sources.StageImportChunkAsync(importId, sequence++, buffer.AsMemory(0, read), timeProvider.GetUtcNow(), cancellationToken);
        }
        if (total == 0) throw new ValidationException("Source import file is empty.");
        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<string> ComputeHashAsync(Stream content, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ChunkSize];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumImportBytes) throw new ValidationException("Source import must not exceed 1 GiB.");
            hash.AppendData(buffer.AsSpan(0, read));
        }
        if (total == 0) throw new ValidationException("Source import file is empty.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string RequestHash(ImportSubmissionSourcesCommand command, string sha256) =>
        Idempotency.HashRequest(new { Command = command with { IdempotencyKey = string.Empty }, Sha256 = sha256 });

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximumLength) throw new ValidationException($"Filter value must not exceed {maximumLength} characters.");
        return normalized.ToLowerInvariant();
    }
}

public sealed record SubmissionSourceImportJobPayload(int Version, Guid ImportId);
public sealed record SubmissionSourceValidationJobPayload(
    int Version,
    Guid RunId,
    Guid? OwnedNetworkProfileId,
    Guid? SubmissionSourceId,
    DateTimeOffset? CursorCreatedAt,
    Guid? CursorId,
    int Remaining);
