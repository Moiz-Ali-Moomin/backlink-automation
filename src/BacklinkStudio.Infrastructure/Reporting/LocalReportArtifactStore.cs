using System.Security.Cryptography;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Infrastructure.Reporting;

public sealed class ReportStorageOptions
{
    public const string SectionName = "Reporting";
    public string StoragePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "reports");
}

public sealed class LocalReportArtifactStore(IOptions<ReportStorageOptions> options) : IReportArtifactStore
{
    private readonly string _root = Path.GetFullPath(options.Value.StoragePath);

    public Task<IReportArtifactWriter> BeginWriteAsync(Guid reportId, ReportFormat format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_root);
        var extension = Extension(format);
        var finalPath = Path.Combine(_root, $"{reportId:N}.{extension}");
        var temporaryPath = Path.Combine(_root, $"{reportId:N}.{Guid.CreateVersion7():N}.tmp");
        IReportArtifactWriter writer = new LocalArtifactWriter(temporaryPath, finalPath);
        return Task.FromResult(writer);
    }

    public Task<Stream?> OpenReadAsync(Guid reportId, ReportFormat format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(_root, $"{reportId:N}.{Extension(format)}");
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    private static string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Json => "json",
        ReportFormat.Csv => "csv",
        ReportFormat.Xlsx => "xlsx",
        ReportFormat.Html => "html",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private sealed class LocalArtifactWriter(string temporaryPath, string finalPath) : IReportArtifactWriter
    {
        private FileStream? _stream = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        private bool _committed;

        public Stream Stream => _stream ?? throw new ObjectDisposedException(nameof(LocalArtifactWriter));

        public async Task<ReportArtifactMetadata> CommitAsync(string artifactName, string contentType, CancellationToken cancellationToken)
        {
            if (_committed || _stream is null) throw new InvalidOperationException("Report artifact writer is already committed or disposed.");

            await _stream.FlushAsync(cancellationToken);
            await _stream.DisposeAsync();
            _stream = null;

            await using var input = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(input, cancellationToken);
            var length = input.Length;
            input.Close();
            File.Move(temporaryPath, finalPath, true);
            _committed = true;
            return new ReportArtifactMetadata(artifactName, contentType, length, Convert.ToHexStringLower(digest));
        }

        public async ValueTask DisposeAsync()
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }
            if (!_committed && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
