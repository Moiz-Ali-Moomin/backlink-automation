using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Discovery;

public sealed class CandidateService(
    IProjectRepository projects,
    ICandidateRepository candidates,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    IUrlNormalizer urlNormalizer,
    TimeProvider timeProvider) : ICandidateService
{
    public async Task<ImportCandidatesResult> ImportAsync(ImportCandidatesCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (command.Urls.Count is < 1 or > 1_000)
        {
            throw new ValidationException("Candidate import must contain between 1 and 1000 URLs.");
        }

        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "candidates_import");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<ImportCandidatesResult>(existing, requestHash);
        }

        var valid = new Dictionary<string, CandidateImportItem>(StringComparer.Ordinal);
        var errors = new List<string>();
        var inputDuplicates = 0;
        for (var index = 0; index < command.Urls.Count; index++)
        {
            var original = command.Urls[index];
            var normalized = urlNormalizer.Normalize(original);
            if (!normalized.IsValid)
            {
                errors.Add($"Item {index}: {normalized.Error}");
                continue;
            }

            var item = new CandidateImportItem(original, normalized.NormalizedUrl!, normalized.Domain!);
            if (!valid.TryAdd(item.NormalizedUrl, item))
            {
                inputDuplicates++;
            }
        }

        var now = timeProvider.GetUtcNow();
        var persisted = await candidates.ImportAsync(command.ProjectId, valid.Values.ToArray(), now, cancellationToken);
        var result = new ImportCandidatesResult(command.Urls.Count, persisted.Accepted, persisted.Duplicates + inputDuplicates, errors.Count, errors.Take(100).ToArray());
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "candidate_import", null, JsonSerializer.Serialize(result), now));
        audit.Append(ProjectService.CreateAudit(actor, "candidates.import", command.ProjectId, null, $"submitted={result.Submitted};accepted={result.Accepted};duplicates={result.Duplicates};invalid={result.Invalid}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<PageResult<CandidateDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var values = await candidates.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var items = values.Take(page.BoundedLimit).ToArray();
        var next = values.Count > page.BoundedLimit && items.Length > 0
            ? CursorCodec.Encode(new PageCursor(items[^1].CreatedAt, items[^1].Id))
            : null;
        return new PageResult<CandidateDto>(items, next);
    }

    public Task<CandidateDto?> GetAsync(Guid id, CancellationToken cancellationToken) => candidates.GetAsync(id, cancellationToken);
}
