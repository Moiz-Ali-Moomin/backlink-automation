using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Submission;

namespace BacklinkStudio.UnitTests;

public sealed class SubmissionContentTests
{
    [Fact]
    public async Task Resolver_IsReproducible_AndResolvesControlledVariants()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var source = new SubmissionSource(projectId, Guid.CreateVersion7(), "https://blog.example/post",
            "https://blog.example/post", "blog.example", "blog.example", OwnershipStatus.Owned, true, null, true, now);
        var identityPool = new SubmissionIdentityPool(projectId, "identities", PoolSelectionStrategy.WeightedDeterministicRandom,
            IdentityEmailStrategy.PlusAddressing, "comments@example.com", null, true, now);
        var identity = new SubmissionIdentity(identityPool.Id, "Studio", "fallback@example.com", null, "BacklinkStudio", true, 3, now);
        var templatePool = new SubmissionTemplatePool(projectId, "comments", SubmissionTemplateType.WordPressComment,
            PoolSelectionStrategy.DeterministicRandom, BacklinkPlacementMethod.CommentBody, true, now);
        var template = new SubmissionTemplate(templatePool.Id, "default", "{{display_name}} links to {{target_url}} from {{source_domain}}.",
            ["Hello"], ["Thanks"], ["BacklinkStudio"], null, true, 1, now);
        var repository = new FakeContentRepository(identityPool, identity, templatePool, template);
        var resolver = new SubmissionContentResolver(repository, new FakeSourceRepository(source));
        var command = new ResolveSubmissionContentCommand(source.Id, Guid.Parse("018f3e86-4900-7a12-a301-123456789abc"),
            identityPool.Id, templatePool.Id, "https://target.example/landing", 1);

        var first = await resolver.ResolveAsync(command, CancellationToken.None);
        var second = await resolver.ResolveAsync(command, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.StartsWith("comments+", first.Email, StringComparison.Ordinal);
        Assert.EndsWith("@example.com", first.Email, StringComparison.Ordinal);
        Assert.Equal("Hello Studio links to https://target.example/landing from blog.example. Thanks", first.ResolvedComment);
        Assert.Equal(BacklinkPlacementMethod.CommentBody, first.PlacementMethod);
    }

    [Fact]
    public void Template_RejectsUnboundedVariantPools()
    {
        var variants = Enumerable.Range(0, 101).Select(x => $"variant-{x}").ToArray();
        Assert.Throws<DomainRuleException>(() => new SubmissionTemplate(Guid.CreateVersion7(), "name", "body", variants,
            null, null, null, true, 1, DateTimeOffset.UtcNow));
    }

    private sealed class FakeContentRepository(
        SubmissionIdentityPool identityPool,
        SubmissionIdentity identity,
        SubmissionTemplatePool templatePool,
        SubmissionTemplate submissionTemplate) : ISubmissionContentRepository
    {
        public void AddIdentityPool(SubmissionIdentityPool pool) => throw new NotSupportedException();
        public Task<SubmissionIdentityPool?> GetIdentityPoolAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionIdentityPool?>(id == identityPool.Id ? identityPool : null);
        public Task<SubmissionIdentityPool?> FindIdentityPoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken) => Task.FromResult<SubmissionIdentityPool?>(null);
        public Task<IReadOnlyList<SubmissionIdentityPool>> ListIdentityPoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionIdentityPool>>([identityPool]);
        public void AddIdentity(SubmissionIdentity value) => throw new NotSupportedException();
        public Task<SubmissionIdentity?> GetIdentityAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionIdentity?>(id == identity.Id ? identity : null);
        public Task<IReadOnlyList<SubmissionIdentity>> ListIdentitiesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionIdentity>>([identity]);
        public void AddTemplatePool(SubmissionTemplatePool pool) => throw new NotSupportedException();
        public Task<SubmissionTemplatePool?> GetTemplatePoolAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionTemplatePool?>(id == templatePool.Id ? templatePool : null);
        public Task<SubmissionTemplatePool?> FindTemplatePoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken) => Task.FromResult<SubmissionTemplatePool?>(null);
        public Task<IReadOnlyList<SubmissionTemplatePool>> ListTemplatePoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionTemplatePool>>([templatePool]);
        public void AddTemplate(SubmissionTemplate value) => throw new NotSupportedException();
        public Task<SubmissionTemplate?> GetTemplateAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionTemplate?>(id == submissionTemplate.Id ? submissionTemplate : null);
        public Task<IReadOnlyList<SubmissionTemplate>> ListTemplatesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionTemplate>>([submissionTemplate]);
    }

    private sealed class FakeSourceRepository(SubmissionSource source) : ISubmissionSourceRepository
    {
        public void AddImport(SubmissionSourceImport sourceImport) => throw new NotSupportedException();
        public Task StageImportChunkAsync(Guid importId, int sequence, ReadOnlyMemory<byte> content, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamImportChunksAsync(Guid importId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<SubmissionSourceImport?> GetImportAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionSourceImport?>(null);
        public Task<SubmissionSourceImport?> FindImportByIdempotencyAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult<SubmissionSourceImport?>(null);
        public Task<SubmissionSourceImportPersistenceResult> ImportBatchAsync(Guid projectId, IReadOnlyList<SubmissionSourceImportItem> items, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountByImportAsync(Guid importId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task DeleteImportChunksAsync(Guid importId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<SubmissionSource?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) => Task.FromResult<SubmissionSource?>(id == source.Id ? source : null);
        public Task<IReadOnlyList<SubmissionSource>> ListAsync(Guid projectId, SubmissionSourceFilter filter, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionSource>>([source]);
        public Task<IReadOnlyList<SubmissionSource>> ListValidationCandidatesAsync(Guid projectId, Guid? ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubmissionSource>>([source]);
    }
}
