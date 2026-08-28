using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Discovery;

public sealed class DiscoveryJobExecutor(
    IDiscoveryRepository discovery,
    ICandidateRepository candidates,
    IEnumerable<IDiscoveryProvider> providers,
    IUrlNormalizer urlNormalizer,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    public JobType JobType => JobType.Discovery;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<DiscoveryJobPayload>(job.Payload);
        if (payload is null || payload.Version != 1)
        {
            throw new ValidationException("Discovery job payload is invalid or unsupported.");
        }

        var state = await discovery.GetTrackedAsync(payload.DiscoveryRunId, cancellationToken) ?? throw new ResourceNotFoundException("DiscoveryRun", payload.DiscoveryRunId);
        if (state.Run.ProjectId != job.ProjectId || state.Query.ProjectId != job.ProjectId)
        {
            throw new ValidationException("Discovery job project does not match its run.");
        }

        if (state.Run.Status == DiscoveryRunStatus.Succeeded)
        {
            return;
        }

        state.Run.Start(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var input = JsonSerializer.Deserialize<DiscoveryInput>(state.Query.InputJson) ?? throw new ValidationException("Stored discovery input is invalid.");
            var providerName = DiscoveryProviderNames.For(state.Query.Provider);
            var provider = providers.SingleOrDefault(x => string.Equals(x.Name, providerName, StringComparison.Ordinal))
                ?? throw new ValidationException($"Discovery provider '{providerName}' is not registered.");
            var batch = await provider.DiscoverAsync(new DiscoveryRequest(job.ProjectId, job.CampaignId, input.Query, input.Content, input.Urls, state.Query.MaximumResults), cancellationToken);
            
            var accepted = new Dictionary<string, CandidateImportItem>(StringComparer.Ordinal);
            var errors = batch.Errors.Take(100).ToList();
            var invalid = 0;
            var blocked = 0;
            var inputDuplicates = 0;

            foreach (var rawUrl in batch.Urls.Take(state.Query.MaximumResults))
            {
                var normalized = urlNormalizer.Normalize(rawUrl);
                if (!normalized.IsValid)
                {
                    invalid++;
                    if (errors.Count < 100)
                    {
                        errors.Add(normalized.Error ?? "Invalid URL.");
                    }
                    continue;
                }

                if (!accepted.TryAdd(normalized.NormalizedUrl!, new CandidateImportItem(rawUrl, normalized.NormalizedUrl!, normalized.Domain!)))
                {
                    inputDuplicates++;
                }
            }

            var now = timeProvider.GetUtcNow();
            var persisted = await candidates.ImportAsync(job.ProjectId, accepted.Values.ToArray(), now, cancellationToken);
            state.Run.Complete(batch.Urls.Count, persisted.Accepted, inputDuplicates + persisted.Duplicates, blocked, invalid, errors, now);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "discovery.complete", job.ProjectId, null, job.Id, job.CorrelationId, $"provider={providerName};discovered={batch.Urls.Count};accepted={persisted.Accepted};duplicates={inputDuplicates + persisted.Duplicates};blocked={blocked};invalid={invalid};errors={errors.Count}", "succeeded", null, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var now = timeProvider.GetUtcNow();
            state.Run.Fail($"{exception.GetType().Name}: {exception.Message}", now);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "discovery.fail", job.ProjectId, null, job.Id, job.CorrelationId, $"provider={DiscoveryProviderNames.For(state.Query.Provider)};errorType={exception.GetType().Name}", "failed", null, now));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}