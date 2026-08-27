using System.Data;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class SubmissionSourceRepository(BacklinkStudioDbContext dbContext) : ISubmissionSourceRepository
{
    public void Add(SubmissionSource source) => dbContext.SubmissionSources.Add(source);

    public void AddImport(SubmissionSourceImport sourceImport) => dbContext.SubmissionSourceImports.Add(sourceImport);

    public async Task StageImportChunkAsync(Guid importId, int sequence, ReadOnlyMemory<byte> content, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (content.Length is < 1 or > 64 * 1_024) throw new ArgumentOutOfRangeException(nameof(content));
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO submission_source_import_chunks (id, import_id, sequence, content, created_at)
            VALUES ({Guid.CreateVersion7(now)}, {importId}, {sequence}, {content.ToArray()}, {now})
            """, cancellationToken);
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamImportChunksAsync(Guid importId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM submission_source_import_chunks WHERE import_id = @import_id ORDER BY sequence";
        command.Parameters.AddWithValue("import_id", importId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) yield return reader.GetFieldValue<byte[]>(0);
    }

    public Task<SubmissionSourceImport?> GetImportAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.SubmissionSourceImports : dbContext.SubmissionSourceImports.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubmissionSourceImport?> FindImportByIdempotencyAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.SubmissionSourceImports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<SubmissionSourceImportPersistenceResult> ImportBatchAsync(Guid projectId, IReadOnlyList<SubmissionSourceImportItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return new(0, 0);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = """
                CREATE TEMP TABLE source_import_batch (
                    id uuid NOT NULL, owned_network_profile_id uuid NULL,
                    original_url text NOT NULL, normalized_url text NOT NULL,
                    domain text NOT NULL, host text NOT NULL, ownership_status text NOT NULL,
                    platform text NOT NULL, cms_type text NOT NULL, automation_permitted boolean NOT NULL,
                    tag text NULL, enabled boolean NOT NULL
                ) ON COMMIT DROP
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync("COPY source_import_batch (id, owned_network_profile_id, original_url, normalized_url, domain, host, ownership_status, platform, cms_type, automation_permitted, tag, enabled) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var item in items)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(Guid.CreateVersion7(now), NpgsqlDbType.Uuid, cancellationToken);
                if (item.OwnedNetworkProfileId is { } profileId)
                    await importer.WriteAsync(profileId, NpgsqlDbType.Uuid, cancellationToken);
                else
                    await importer.WriteNullAsync(cancellationToken);
                await importer.WriteAsync(item.OriginalUrl, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.NormalizedUrl, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.Domain, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.Host, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.OwnershipStatus.ToString(), NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.Platform.ToString(), NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.CmsType.ToString(), NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.AutomationPermitted, NpgsqlDbType.Boolean, cancellationToken);
                if (item.Tag is null) await importer.WriteNullAsync(cancellationToken);
                else await importer.WriteAsync(item.Tag, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(item.Enabled, NpgsqlDbType.Boolean, cancellationToken);
            }
            await importer.CompleteAsync(cancellationToken);
        }

        int accepted;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO submission_sources (
                    id, project_id, owned_network_profile_id, source_import_id, original_url, normalized_url, domain, host,
                    platform, cms_type, opportunity_type, ownership_status, automation_permitted,
                    technical_compatibility, validation_status, requires_browser, requires_authentication,
                    requires_manual_action, supports_wordpress_comment, supports_owned_wordpress_api,
                    supports_owned_property_placement, redirect_chain, comments_enabled, additional_required_fields,
                    requires_cookies, requires_nonce, tag, success_count, failure_count,
                    pending_moderation_count, verified_count, lost_count, enabled, created_at, updated_at)
                SELECT id, @project_id, owned_network_profile_id, @import_id, original_url, normalized_url, domain, host,
                    platform, cms_type, 'Unknown', ownership_status, automation_permitted,
                    'Unknown', 'Pending', false, false, false, false, false, false, ARRAY[]::text[], false,
                    ARRAY[]::text[], false, false, tag,
                    0, 0, 0, 0, 0, enabled, @now, @now
                FROM source_import_batch
                ON CONFLICT (project_id, normalized_url) DO NOTHING
                """;
            insert.Parameters.AddWithValue("project_id", projectId);
            insert.Parameters.AddWithValue("import_id", items.Count > 0 ? items[0].SourceImportId : throw new InvalidOperationException());
            insert.Parameters.AddWithValue("now", now);
            accepted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(accepted, items.Count - accepted);
    }

    public Task<int> CountByImportAsync(Guid importId, CancellationToken cancellationToken) =>
        dbContext.SubmissionSources.CountAsync(x => x.SourceImportId == importId, cancellationToken);

    public Task DeleteImportChunksAsync(Guid importId, CancellationToken cancellationToken) =>
        dbContext.SubmissionSourceImportChunks.Where(x => x.ImportId == importId).ExecuteDeleteAsync(cancellationToken);

    public Task<SubmissionSource?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.SubmissionSources : dbContext.SubmissionSources.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubmissionSource?> FindByNormalizedUrlAsync(Guid projectId, string normalizedUrl, bool tracked,
        CancellationToken cancellationToken) =>
        (tracked ? dbContext.SubmissionSources : dbContext.SubmissionSources.AsNoTracking())
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.NormalizedUrl == normalizedUrl, cancellationToken);

    public async Task<IReadOnlyList<SubmissionSource>> ListByImportAsync(Guid sourceImportId, int take,
        CancellationToken cancellationToken) => await dbContext.SubmissionSources
        .Where(x => x.SourceImportId == sourceImportId)
        .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubmissionSource>> ListAsync(Guid projectId, SubmissionSourceFilter filter, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.SubmissionSources.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (filter.OwnedNetworkProfileId is not null) query = query.Where(x => x.OwnedNetworkProfileId == filter.OwnedNetworkProfileId);
        if (filter.Domain is not null) query = query.Where(x => x.Domain == filter.Domain);
        if (filter.Platform is not null) query = query.Where(x => x.Platform == filter.Platform);
        if (filter.CmsType is not null) query = query.Where(x => x.CmsType == filter.CmsType);
        if (filter.AdapterName is not null) query = query.Where(x => x.AdapterName == filter.AdapterName);
        if (filter.OwnershipStatus is not null) query = query.Where(x => x.OwnershipStatus == filter.OwnershipStatus);
        if (filter.AutomationPermitted is not null) query = query.Where(x => x.AutomationPermitted == filter.AutomationPermitted);
        if (filter.TechnicalCompatibility is not null) query = query.Where(x => x.TechnicalCompatibility == filter.TechnicalCompatibility);
        if (filter.ValidationStatus is not null) query = query.Where(x => x.ValidationStatus == filter.ValidationStatus);
        if (filter.Enabled is not null) query = query.Where(x => x.Enabled == filter.Enabled);
        if (filter.Tag is not null) query = query.Where(x => x.Tag == filter.Tag);
        if (filter.PreviousSubmissionStatus is not null)
        {
            query = query.Where(source => dbContext.SubmissionJobs.Any(submission =>
                submission.SubmissionSourceId == source.Id && submission.Status == filter.PreviousSubmissionStatus));
        }
        if (filter.PreviousVerificationStatus is not null)
        {
            query = query.Where(source => dbContext.Backlinks.Any(backlink =>
                backlink.SubmissionSourceId == source.Id && backlink.Status == filter.PreviousVerificationStatus));
        }
        if (cursor is not null)
        {
            var value = cursor.Value;
            query = query.Where(x => x.CreatedAt > value.CreatedAt || x.CreatedAt == value.CreatedAt && x.Id.CompareTo(value.Id) > 0);
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubmissionSource>> ListValidationCandidatesAsync(Guid projectId, Guid? ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.SubmissionSources.Where(x => x.ProjectId == projectId && x.Enabled);
        if (ownedNetworkProfileId is not null) query = query.Where(x => x.OwnedNetworkProfileId == ownedNetworkProfileId);
        if (cursor is not null)
        {
            var value = cursor.Value;
            query = query.Where(x => x.CreatedAt > value.CreatedAt || x.CreatedAt == value.CreatedAt && x.Id.CompareTo(value.Id) > 0);
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }
}
