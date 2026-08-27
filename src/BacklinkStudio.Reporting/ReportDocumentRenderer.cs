using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Reporting;

public sealed record ReportRenderResult(string ArtifactName, string ContentType, int RowCount);

public static class ReportDocumentRenderer
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const int MaximumXlsxDataRowsPerSheet = 1_048_575;
    private const int IntegerSummaryFieldCount = 20;
    private static readonly string[] SummaryHeaders = ["Candidates", "Eligible", "Approved", "Queued", "Processing", "Submitted", "Pending Moderation", "Verified", "Rejected", "Failed", "Lost", "Follow", "Nofollow", "UGC", "Sponsored", "Approved Submissions", "Duplicate", "Pending Verification", "Missing", "Verification Error", "Attempt Count", "Submission Success Rate %", "Verification Rate %"];
    private static readonly string[] PerformanceHeaders = ["Dimension", "Value", "Attempt Count", "Successful Submissions", "Verified Backlinks", "Submission Success Rate %", "Verification Rate %"];
    private static readonly string[] DetailHeaders = ["Source URL", "Domain", "Network", "Platform", "CMS", "Target URL", "Identity", "Template", "Opportunity Type", "Submission Status", "Moderation Status", "Verification Status", "Anchor", "rel", "HTTP Status", "Queued At", "Started At", "Completed At", "Submitted At", "First Seen", "Last Seen", "Last Checked", "Error"];
    private static readonly string[] SummarySheetNames = ["Summary", "Performance"];

    public static Task<ReportRenderResult> RenderAsync(Report report, ReportSummaryDto summary, IAsyncEnumerable<ReportRowDto> rows, Stream output, CancellationToken cancellationToken) =>
        report.Format switch
        {
            ReportFormat.Json => RenderJsonAsync(report, summary, rows, output, cancellationToken),
            ReportFormat.Csv => RenderCsvAsync(report, summary, rows, output, cancellationToken),
            ReportFormat.Xlsx => RenderXlsxAsync(report, summary, rows, output, cancellationToken),
            ReportFormat.Html => RenderHtmlAsync(report, summary, rows, output, cancellationToken),
            _ => throw new ValidationException("Report format is unsupported.")
        };

    private static async Task<ReportRenderResult> RenderJsonAsync(Report report, ReportSummaryDto summary, IAsyncEnumerable<ReportRowDto> rows, Stream output, CancellationToken cancellationToken)
    {
        var count = 0;
        await using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("reportId", report.Id);
        json.WriteString("kind", ToCamelCase(report.Kind.ToString()));
        json.WriteString("createdAt", report.CreatedAt);
        json.WritePropertyName("summary");
        WriteSummaryJson(json, summary);
        json.WriteStartArray("details");
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            WriteRowJson(json, row);
            count++;
            if (count % 1_000 == 0) await json.FlushAsync(cancellationToken);
        }
        json.WriteEndArray();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
        return Result(report, "application/json", "json", count);
    }

    private static async Task<ReportRenderResult> RenderCsvAsync(Report report, ReportSummaryDto summary, IAsyncEnumerable<ReportRowDto> rows, Stream output, CancellationToken cancellationToken)
    {
        var count = 0;
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), 64 * 1024, leaveOpen: true);
        await WriteCsvRowAsync(writer, SummaryHeaders, cancellationToken);
        await WriteCsvRowAsync(writer, SummaryValues(summary), cancellationToken);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken);
        await WriteCsvRowAsync(writer, PerformanceHeaders, cancellationToken);
        await foreach (var performance in PerformanceRows(summary).WithCancellation(cancellationToken))
            await WriteCsvRowAsync(writer, performance, cancellationToken);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken);
        await WriteCsvRowAsync(writer, DetailHeaders, cancellationToken);
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await WriteCsvRowAsync(writer, RowValues(row), cancellationToken);
            count++;
        }
        await writer.FlushAsync(cancellationToken);
        return Result(report, "text/csv; charset=utf-8", "csv", count);
    }

    private static async Task<ReportRenderResult> RenderHtmlAsync(Report report, ReportSummaryDto summary, IAsyncEnumerable<ReportRowDto> rows, Stream output, CancellationToken cancellationToken)
    {
        var count = 0;
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        await writer.WriteAsync("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>BacklinkStudio Report</title><style>body{font-family:system-ui,sans-serif;margin:2rem;color:#17202a}table{border-collapse:collapse;width:100%;margin-bottom:2rem}th,td{border:1px solid #ccd1d1;padding:.45rem;text-align:left;vertical-align:top}th{background:#f4f6f7}caption{text-align:left;font-size:1.2rem;font-weight:700;margin:.5rem 0}</style></head><body>");
        await writer.WriteAsync($"<h1>BacklinkStudio {WebUtility.HtmlEncode(report.Kind.ToString())} Report</h1><p>Report ID: {report.Id:D}</p><table><caption>Summary</caption><thead><tr>");
        foreach (var header in SummaryHeaders) await writer.WriteAsync($"<th>{WebUtility.HtmlEncode(header)}</th>");
        await writer.WriteAsync("</tr></thead><tbody><tr>");
        foreach (var value in SummaryValues(summary)) await writer.WriteAsync($"<td>{WebUtility.HtmlEncode(value)}</td>");
        await writer.WriteAsync("</tr></tbody></table><table><caption>Performance by domain, template, and identity (top 100 per dimension)</caption><thead><tr>");
        foreach (var header in PerformanceHeaders) await writer.WriteAsync($"<th>{WebUtility.HtmlEncode(header)}</th>");
        await writer.WriteAsync("</tr></thead><tbody>");
        await foreach (var performance in PerformanceRows(summary).WithCancellation(cancellationToken))
        {
            await writer.WriteAsync("<tr>");
            foreach (var value in performance) await writer.WriteAsync($"<td>{WebUtility.HtmlEncode(value)}</td>");
            await writer.WriteAsync("</tr>");
        }
        await writer.WriteAsync("</tbody></table><table><caption>Details</caption><thead><tr>");
        foreach (var header in DetailHeaders) await writer.WriteAsync($"<th>{WebUtility.HtmlEncode(header)}</th>");
        await writer.WriteAsync("</tr></thead><tbody>");
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteAsync("<tr>");
            foreach (var value in RowValues(row)) await writer.WriteAsync($"<td>{WebUtility.HtmlEncode(value)}</td>");
            await writer.WriteAsync("</tr>");
            count++;
        }
        await writer.WriteAsync("</tbody></table></body></html>");
        await writer.FlushAsync(cancellationToken);
        return Result(report, "text/html; charset=utf-8", "html", count);
    }

    private static async Task<ReportRenderResult> RenderXlsxAsync(Report report, ReportSummaryDto summary, IAsyncEnumerable<ReportRowDto> rows, Stream output, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteWorksheetAsync(archive, "xl/worksheets/sheet1.xml", SummaryHeaders, OneRow(SummaryValues(summary)), cancellationToken, includeAutoFilter: true);
        await WriteWorksheetAsync(archive, "xl/worksheets/sheet2.xml", PerformanceHeaders, PerformanceRows(summary), cancellationToken, includeAutoFilter: true);

        var detailSheetNames = new List<string>();
        var rowCount = 0;
        var sheetNumber = 3;
        await using var enumerator = rows.GetAsyncEnumerator(cancellationToken);
        var hasRow = await enumerator.MoveNextAsync();
        do
        {
            var sheetName = detailSheetNames.Count == 0 ? "Details" : $"Details {detailSheetNames.Count + 1}";
            detailSheetNames.Add(sheetName);
            var entry = archive.CreateEntry($"xl/worksheets/sheet{sheetNumber}.xml", CompressionLevel.Fastest);
            await using var stream = entry.Open();
            using var xml = CreateXmlWriter(stream);
            await StartWorksheetAsync(xml);
            await WriteXmlRowAsync(xml, DetailHeaders, cancellationToken);
            var sheetRows = 0;
            while (hasRow && sheetRows < MaximumXlsxDataRowsPerSheet)
            {
                await WriteXmlRowAsync(xml, RowValues(enumerator.Current), cancellationToken);
                sheetRows++;
                rowCount++;
                hasRow = await enumerator.MoveNextAsync();
            }
            await EndWorksheetAsync(xml, $"A1:{ExcelColumnName(DetailHeaders.Length)}{sheetRows + 1}");
            sheetNumber++;
        } while (hasRow);

        var sheets = SummarySheetNames.Concat(detailSheetNames).ToArray();
        await WriteWorkbookAsync(archive, sheets, cancellationToken);
        await WriteWorkbookRelationshipsAsync(archive, sheets.Length, cancellationToken);
        await WriteRootRelationshipsAsync(archive, cancellationToken);
        await WriteContentTypesAsync(archive, sheets.Length, cancellationToken);
        return Result(report, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx", rowCount);
    }

    private static async Task WriteWorksheetAsync(ZipArchive archive, string path, string[] headers, IAsyncEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken, bool includeAutoFilter = false)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        using var xml = CreateXmlWriter(stream);
        await StartWorksheetAsync(xml);
        await WriteXmlRowAsync(xml, headers, cancellationToken);
        var rowCount = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await WriteXmlRowAsync(xml, row, cancellationToken);
            rowCount++;
        }
        await EndWorksheetAsync(xml, includeAutoFilter ? $"A1:{ExcelColumnName(headers.Length)}{rowCount + 1}" : null);
    }

    private static XmlWriter CreateXmlWriter(Stream stream) => XmlWriter.Create(stream, new XmlWriterSettings
    {
        Async = true,
        Encoding = new UTF8Encoding(false),
        CloseOutput = false,
        Indent = false
    });

    private static async Task StartWorksheetAsync(XmlWriter xml)
    {
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "worksheet", SpreadsheetNamespace);
        await xml.WriteStartElementAsync(null, "sheetData", SpreadsheetNamespace);
    }

    private static async Task EndWorksheetAsync(XmlWriter xml, string? autoFilterRange = null)
    {
        await xml.WriteEndElementAsync();
        if (autoFilterRange is not null)
        {
            await xml.WriteStartElementAsync(null, "autoFilter", SpreadsheetNamespace);
            await xml.WriteAttributeStringAsync(null, "ref", null, autoFilterRange);
            await xml.WriteEndElementAsync();
        }
        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
        await xml.FlushAsync();
    }

    private static async Task WriteXmlRowAsync(XmlWriter xml, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await xml.WriteStartElementAsync(null, "row", SpreadsheetNamespace);
        foreach (var value in values)
        {
            await xml.WriteStartElementAsync(null, "c", SpreadsheetNamespace);
            await xml.WriteAttributeStringAsync(null, "t", null, "inlineStr");
            await xml.WriteStartElementAsync(null, "is", SpreadsheetNamespace);
            await xml.WriteStartElementAsync(null, "t", SpreadsheetNamespace);
            await xml.WriteAttributeStringAsync("xml", "space", XmlNamespace, "preserve");
            await xml.WriteStringAsync(SanitizeXml(value));
            await xml.WriteEndElementAsync();
            await xml.WriteEndElementAsync();
            await xml.WriteEndElementAsync();
        }
        await xml.WriteEndElementAsync();
    }

    private static async Task WriteWorkbookAsync(ZipArchive archive, string[] sheetNames, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("xl/workbook.xml", CompressionLevel.Fastest);
        await using var stream = entry.Open();
        using var xml = CreateXmlWriter(stream);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "workbook", SpreadsheetNamespace);
        await xml.WriteAttributeStringAsync("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        await xml.WriteStartElementAsync(null, "sheets", SpreadsheetNamespace);
        for (var index = 0; index < sheetNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await xml.WriteStartElementAsync(null, "sheet", SpreadsheetNamespace);
            await xml.WriteAttributeStringAsync(null, "name", null, sheetNames[index]);
            await xml.WriteAttributeStringAsync(null, "sheetId", null, (index + 1).ToString(CultureInfo.InvariantCulture));
            await xml.WriteAttributeStringAsync("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", $"rId{index + 1}");
            await xml.WriteEndElementAsync();
        }
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
    }

    private static async Task WriteWorkbookRelationshipsAsync(ZipArchive archive, int sheetCount, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("xl/_rels/workbook.xml.rels", CompressionLevel.Fastest);
        await using var stream = entry.Open();
        using var xml = CreateXmlWriter(stream);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "Relationships", PackageRelationshipsNamespace);
        for (var index = 1; index <= sheetCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await xml.WriteStartElementAsync(null, "Relationship", PackageRelationshipsNamespace);
            await xml.WriteAttributeStringAsync(null, "Id", null, $"rId{index}");
            await xml.WriteAttributeStringAsync(null, "Type", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            await xml.WriteAttributeStringAsync(null, "Target", null, $"worksheets/sheet{index}.xml");
            await xml.WriteEndElementAsync();
        }
        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
    }

    private static Task WriteRootRelationshipsAsync(ZipArchive archive, CancellationToken cancellationToken) => WriteTextEntryAsync(archive, "_rels/.rels", """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """, cancellationToken);

    private static async Task WriteContentTypesAsync(ZipArchive archive, int sheetCount, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (var index = 1; index <= sheetCount; index++) builder.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        builder.Append("</Types>");
        await WriteTextEntryAsync(archive, "[Content_Types].xml", builder.ToString(), cancellationToken);
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4_096, leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static void WriteSummaryJson(Utf8JsonWriter json, ReportSummaryDto value)
    {
        json.WriteStartObject();
        var values = SummaryValues(value);
        for (var index = 0; index < IntegerSummaryFieldCount; index++)
            json.WriteNumber(ToCamelCase(SummaryHeaders[index]), int.Parse(values[index], CultureInfo.InvariantCulture));
        json.WriteNumber("attemptCount", value.AttemptCount);
        json.WriteNumber("submissionSuccessRate", value.SubmissionSuccessRate);
        json.WriteNumber("verificationRate", value.VerificationRate);
        WritePerformanceJson(json, "domainPerformance", value.DomainPerformance);
        WritePerformanceJson(json, "templatePerformance", value.TemplatePerformance);
        WritePerformanceJson(json, "identityPerformance", value.IdentityPerformance);
        json.WriteEndObject();
    }

    private static void WritePerformanceJson(Utf8JsonWriter json, string name,
        IReadOnlyList<ReportPerformanceBreakdownDto>? values)
    {
        json.WriteStartArray(name);
        foreach (var value in values ?? [])
        {
            json.WriteStartObject();
            json.WriteString("key", value.Key);
            json.WriteNumber("attemptCount", value.AttemptCount);
            json.WriteNumber("successfulSubmissions", value.SuccessfulSubmissions);
            json.WriteNumber("verifiedBacklinks", value.VerifiedBacklinks);
            json.WriteNumber("submissionSuccessRate", value.SubmissionSuccessRate);
            json.WriteNumber("verificationRate", value.VerificationRate);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteRowJson(Utf8JsonWriter json, ReportRowDto row)
    {
        json.WriteStartObject();
        json.WriteString("sourceUrl", row.SourceUrl);
        WriteNullableString(json, "domain", row.Domain);
        WriteNullableString(json, "network", row.Network);
        WriteNullableString(json, "platform", row.Platform is null ? null : ToCamelCase(row.Platform.ToString()!));
        WriteNullableString(json, "cms", row.CmsType is null ? null : ToCamelCase(row.CmsType.ToString()!));
        json.WriteString("targetUrl", row.TargetUrl);
        WriteNullableString(json, "identity", row.Identity);
        WriteNullableString(json, "template", row.Template);
        WriteNullableString(json, "anchor", row.Anchor);
        WriteNullableString(json, "opportunityType", row.OpportunityType is null ? null : ToCamelCase(row.OpportunityType.ToString()!));
        WriteNullableString(json, "submissionStatus", row.SubmissionStatus is null ? null : ToCamelCase(row.SubmissionStatus.ToString()!));
        WriteNullableString(json, "moderationStatus", row.ModerationStatus is null ? null : ToCamelCase(row.ModerationStatus.ToString()!));
        WriteNullableString(json, "verificationStatus", row.VerificationStatus is null ? null : ToCamelCase(row.VerificationStatus.ToString()!));
        json.WriteStartArray("rel");
        foreach (var value in row.Rel) json.WriteStringValue(value);
        json.WriteEndArray();
        if (row.HttpStatus is null) json.WriteNull("httpStatus"); else json.WriteNumber("httpStatus", row.HttpStatus.Value);
        WriteNullableDate(json, "queuedAt", row.QueuedAt);
        WriteNullableDate(json, "startedAt", row.StartedAt);
        WriteNullableDate(json, "completedAt", row.CompletedAt);
        WriteNullableDate(json, "submittedAt", row.SubmittedAt);
        WriteNullableDate(json, "firstSeenAt", row.FirstSeenAt);
        WriteNullableDate(json, "lastSeenAt", row.LastSeenAt);
        WriteNullableDate(json, "lastCheckedAt", row.LastCheckedAt);
        WriteNullableString(json, "error", row.Error);
        json.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null) json.WriteNull(name); else json.WriteString(name, value);
    }

    private static void WriteNullableDate(Utf8JsonWriter json, string name, DateTimeOffset? value)
    {
        if (value is null) json.WriteNull(name); else json.WriteString(name, value.Value);
    }

    private static async Task WriteCsvRowAsync(StreamWriter writer, IReadOnlyList<string> values, CancellationToken cancellationToken) =>
        await writer.WriteLineAsync(string.Join(',', values.Select(EscapeCsv)).AsMemory(), cancellationToken);

    private static string EscapeCsv(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        var safe = trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' ? $"'{value}" : value;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string[] SummaryValues(ReportSummaryDto value) =>
    [
        Number(value.Candidates), Number(value.Eligible), Number(value.Approved), Number(value.Queued), Number(value.Processing), Number(value.Submitted), Number(value.Pending), Number(value.Verified), Number(value.Rejected), Number(value.Failed), Number(value.Lost), Number(value.Follow), Number(value.Nofollow), Number(value.Ugc), Number(value.Sponsored), Number(value.ApprovedSubmissions), Number(value.Duplicate), Number(value.PendingVerification), Number(value.Missing), Number(value.VerificationError), Number(value.AttemptCount), Rate(value.SubmissionSuccessRate), Rate(value.VerificationRate)
    ];

    private static async IAsyncEnumerable<IReadOnlyList<string>> PerformanceRows(ReportSummaryDto summary)
    {
        foreach (var row in summary.DomainPerformance ?? []) yield return PerformanceValues("Domain", row);
        foreach (var row in summary.TemplatePerformance ?? []) yield return PerformanceValues("Template", row);
        foreach (var row in summary.IdentityPerformance ?? []) yield return PerformanceValues("Identity", row);
        await Task.CompletedTask;
    }

    private static string[] PerformanceValues(string dimension, ReportPerformanceBreakdownDto value) =>
    [
        dimension, value.Key, Number(value.AttemptCount), Number(value.SuccessfulSubmissions), Number(value.VerifiedBacklinks),
        Rate(value.SubmissionSuccessRate), Rate(value.VerificationRate)
    ];

    private static string[] RowValues(ReportRowDto row) =>
    [
        row.SourceUrl,
        row.Domain ?? string.Empty,
        row.Network ?? string.Empty,
        row.Platform?.ToString() ?? string.Empty,
        row.CmsType?.ToString() ?? string.Empty,
        row.TargetUrl,
        row.Identity ?? string.Empty,
        row.Template ?? string.Empty,
        row.OpportunityType?.ToString() ?? string.Empty,
        row.SubmissionStatus?.ToString() ?? string.Empty,
        row.ModerationStatus?.ToString() ?? string.Empty,
        row.VerificationStatus?.ToString() ?? string.Empty,
        row.Anchor ?? string.Empty,
        string.Join(' ', row.Rel),
        row.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Date(row.QueuedAt),
        Date(row.StartedAt),
        Date(row.CompletedAt),
        Date(row.SubmittedAt),
        Date(row.FirstSeenAt),
        Date(row.LastSeenAt),
        Date(row.LastCheckedAt),
        row.Error ?? string.Empty
    ];

    private static async IAsyncEnumerable<IReadOnlyList<string>> OneRow(IReadOnlyList<string> row)
    {
        yield return row;
        await Task.CompletedTask;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Rate(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Date(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string ToCamelCase(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        var first = char.ToLowerInvariant(parts[0][0]) + parts[0][1..];
        return first + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
    private static string SanitizeXml(string value) => new(value.Where(XmlConvert.IsXmlChar).ToArray());
    private static string ExcelColumnName(int number)
    {
        var result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }
    private static ReportRenderResult Result(Report report, string contentType, string extension, int rowCount) => new($"backlinkstudio-{report.Kind.ToString().ToLowerInvariant()}-{report.Id:N}.{extension}", contentType, rowCount);
}
