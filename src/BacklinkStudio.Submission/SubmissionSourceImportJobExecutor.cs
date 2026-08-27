using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class SubmissionSourceImportJobExecutor(
    ISubmissionSourceRepository sources,
    IOwnedNetworkExecutionAuthorizer authorizer,
    IUrlNormalizer urlNormalizer,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    private const int BatchSize = 1_000;
    private const int MaximumCsvRecordLength = 64 * 1_024;

    public JobType JobType => JobType.SubmissionSourceImport;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SubmissionSourceImportJobPayload>(job.Payload);
        if (payload?.Version != 1) throw new ValidationException("Submission source import payload is invalid or unsupported.");
        var sourceImport = await sources.GetImportAsync(payload.ImportId, true, cancellationToken)
            ?? throw new ValidationException("Submission source import is missing.");
        if (sourceImport.ProjectId != job.ProjectId || sourceImport.JobId != job.Id)
            throw new ValidationException("Submission source import does not match its durable job.");
        if (sourceImport.Status == SubmissionSourceImportStatus.Completed) return;
        sourceImport.Start(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counters = new ImportCounters();
        try
        {
            await using var content = new ChunkedImportStream(sources.StreamImportChunksAsync(sourceImport.Id, cancellationToken));
            using var reader = new StreamReader(content, new UTF8Encoding(false, true), true, 16 * 1_024, leaveOpen: true);
            if (sourceImport.Format == SubmissionSourceImportFormat.Txt)
                await ImportTxtAsync(reader, sourceImport, counters, cancellationToken);
            else
                await ImportCsvAsync(reader, sourceImport, counters, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            sourceImport.Fail("The import is not valid UTF-8.", timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ValidationException("The import is not valid UTF-8.");
        }

        var accepted = await sources.CountByImportAsync(sourceImport.Id, cancellationToken);
        var duplicates = Math.Max(0, counters.Valid - accepted);
        var completedAt = timeProvider.GetUtcNow();
        sourceImport.Complete(counters.Total, accepted, duplicates, counters.Invalid, counters.Errors, completedAt);
        BacklinkStudioTelemetry.BacklinkSourcesImported.Add(accepted,
            new KeyValuePair<string, object?>("source.format", sourceImport.Format.ToString()));
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission_sources.import_complete", job.ProjectId,
            null, job.Id, job.CorrelationId,
            $"importId={sourceImport.Id};total={counters.Total};accepted={accepted};duplicates={duplicates};invalid={counters.Invalid};errors={counters.Errors}",
            "succeeded", null, completedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sources.DeleteImportChunksAsync(sourceImport.Id, cancellationToken);
    }

    private async Task ImportTxtAsync(StreamReader reader, SubmissionSourceImport sourceImport,
        ImportCounters counters, CancellationToken cancellationToken)
    {
        var batch = new List<SubmissionSourceImportItem>(BatchSize);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            counters.Total++;
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#')) continue;
            await AddItemAsync(value, sourceImport.Tag, true, SourcePlatform.Unknown, CmsType.Unknown, sourceImport,
                batch, counters, cancellationToken);
            if (batch.Count == BatchSize) await FlushAsync(sourceImport, batch, cancellationToken);
        }
        await FlushAsync(sourceImport, batch, cancellationToken);
    }

    private async Task ImportCsvAsync(StreamReader reader, SubmissionSourceImport sourceImport,
        ImportCounters counters, CancellationToken cancellationToken)
    {
        var headerRecord = await ReadCsvRecordAsync(reader, cancellationToken)
            ?? throw new ValidationException("CSV import requires a header row.");
        var headers = headerRecord.Select(x => x.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToArray();
        if (headers.Length is < 1 or > 64 || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            throw new ValidationException("CSV headers must be unique and contain at most 64 columns.");
        var urlIndex = Array.IndexOf(headers, "url");
        if (urlIndex < 0) throw new ValidationException("CSV import requires a url column.");
        var tagIndex = Array.IndexOf(headers, "tag");
        var enabledIndex = Array.IndexOf(headers, "enabled");
        var platformIndex = Array.IndexOf(headers, "platform");
        var cmsIndex = Array.IndexOf(headers, "cms");
        var batch = new List<SubmissionSourceImportItem>(BatchSize);

        while (await ReadCsvRecordAsync(reader, cancellationToken) is { } row)
        {
            counters.Total++;
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count != headers.Length || string.IsNullOrWhiteSpace(row[urlIndex]))
            {
                counters.Invalid++;
                continue;
            }
            var enabled = true;
            if (enabledIndex >= 0 && !ParseEnabled(row[enabledIndex], out enabled))
            {
                counters.Invalid++;
                continue;
            }
            var tag = tagIndex >= 0 && !string.IsNullOrWhiteSpace(row[tagIndex]) ? row[tagIndex].Trim() : sourceImport.Tag;
            if (tag?.Length > 100)
            {
                counters.Invalid++;
                continue;
            }
            var platform = platformIndex >= 0 && Enum.TryParse<SourcePlatform>(row[platformIndex], true, out var parsedPlatform)
                ? parsedPlatform : SourcePlatform.Unknown;
            var cms = cmsIndex >= 0 && Enum.TryParse<CmsType>(row[cmsIndex], true, out var parsedCms)
                ? parsedCms : CmsType.Unknown;
            await AddItemAsync(row[urlIndex], tag, enabled, platform, cms, sourceImport, batch, counters,
                cancellationToken);
            if (batch.Count == BatchSize) await FlushAsync(sourceImport, batch, cancellationToken);
        }
        await FlushAsync(sourceImport, batch, cancellationToken);
    }

    private async Task AddItemAsync(string value, string? tag, bool enabled, SourcePlatform platform, CmsType cms,
        SubmissionSourceImport sourceImport, List<SubmissionSourceImportItem> batch, ImportCounters counters,
        CancellationToken cancellationToken)
    {
        var normalized = urlNormalizer.Normalize(value);
        if (!normalized.IsValid)
        {
            counters.Invalid++;
            return;
        }
        var host = new Uri(normalized.NormalizedUrl!, UriKind.Absolute).IdnHost.ToLowerInvariant();
        var authorization = await authorizer.AuthorizeSourceAsync(sourceImport.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest(host, sourceImport.OwnedNetworkProfileId, enabled),
            cancellationToken);
        var profile = authorization.Allowed ? authorization.Profile : null;
        batch.Add(new SubmissionSourceImportItem(sourceImport.Id, profile?.Id, value.Trim(), normalized.NormalizedUrl!,
            normalized.Domain!, host, platform, cms, profile?.OwnershipStatus ?? OwnershipStatus.Unverified,
            profile is not null && !authorization.TestOwnershipOverrideApplied,
            tag, enabled));
        counters.Valid++;
    }

    private async Task FlushAsync(SubmissionSourceImport sourceImport, List<SubmissionSourceImportItem> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;
        await sources.ImportBatchAsync(sourceImport.ProjectId, batch, timeProvider.GetUtcNow(), cancellationToken);
        batch.Clear();
    }

    private static bool ParseEnabled(string value, out bool enabled)
    {
        if (string.IsNullOrWhiteSpace(value)) { enabled = true; return true; }
        return TryParseBoolean(value, out enabled);
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes": result = true; return true;
            case "false" or "0" or "no": result = false; return true;
            default: result = false; return false;
        }
    }

    private static async Task<IReadOnlyList<string>?> ReadCsvRecordAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        StringBuilder? record = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            record ??= new StringBuilder();
            if (record.Length > 0) record.Append('\n');
            record.Append(line);
            if (record.Length > MaximumCsvRecordLength) throw new ValidationException("CSV record must not exceed 65536 characters.");
            var state = ParseCsv(record.ToString(), out var fields);
            if (state == CsvParseState.Complete) return fields;
            if (state == CsvParseState.Invalid) throw new ValidationException("CSV contains an invalid quoted field.");
        }
        if (record is null) return null;
        throw new ValidationException("CSV ends inside a quoted field.");
    }

    private static CsvParseState ParseCsv(string value, out IReadOnlyList<string> fields)
    {
        var result = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var quoteClosed = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (character != '"') { field.Append(character); continue; }
                if (index + 1 < value.Length && value[index + 1] == '"') { field.Append('"'); index++; continue; }
                quoted = false;
                quoteClosed = true;
                continue;
            }
            if (character == ',' && !quoted)
            {
                result.Add(field.ToString());
                field.Clear();
                quoteClosed = false;
                continue;
            }
            if (character == '"')
            {
                if (field.Length != 0 || quoteClosed) { fields = []; return CsvParseState.Invalid; }
                quoted = true;
                continue;
            }
            if (quoteClosed && !char.IsWhiteSpace(character)) { fields = []; return CsvParseState.Invalid; }
            if (!quoteClosed) field.Append(character);
        }
        if (quoted) { fields = []; return CsvParseState.Incomplete; }
        result.Add(field.ToString());
        fields = result;
        return CsvParseState.Complete;
    }

    private sealed class ImportCounters
    {
        public int Total { get; set; }
        public int Valid { get; set; }
        public int Invalid { get; set; }
        public int Errors { get; set; }
    }

    private enum CsvParseState { Complete, Incomplete, Invalid }
}

internal sealed class ChunkedImportStream(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks) : Stream
{
    private readonly IAsyncEnumerator<ReadOnlyMemory<byte>> _enumerator = chunks.GetAsyncEnumerator();
    private ReadOnlyMemory<byte> _current;
    private int _offset;
    private bool _finished;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!_finished)
        {
            if (_offset < _current.Length)
            {
                var count = Math.Min(buffer.Length, _current.Length - _offset);
                _current.Slice(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }
            if (!await _enumerator.MoveNextAsync()) { _finished = true; break; }
            _current = _enumerator.Current;
            _offset = 0;
        }
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask DisposeAsync()
    {
        await _enumerator.DisposeAsync();
        await base.DisposeAsync();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
