using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Reporting;

namespace BacklinkStudio.UnitTests;

public sealed class ReportRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReportSummaryDto Summary = new(
        10, 9, 8, 1, 2, 3, 4, 5, 6, 7, 8, 4, 1, 2, 3,
        AttemptCount: 12,
        SubmissionSuccessRate: 75,
        VerificationRate: 50,
        DomainPerformance: [new("source.example", 4, 3, 2, 75, 50)],
        TemplatePerformance: [new("Brand comment", 3, 2, 1, 66.67, 33.33)],
        IdentityPerformance: [new("Brand", 2, 2, 1, 100, 50)]);

    [Theory]
    [InlineData(ReportFormat.Json)]
    [InlineData(ReportFormat.Csv)]
    [InlineData(ReportFormat.Xlsx)]
    [InlineData(ReportFormat.Html)]
    public async Task Renderer_ProducesRequestedFormatWithSummaryAndSafeDetails(ReportFormat format)
    {
        var report = new Report(Guid.CreateVersion7(Now), null, ReportKind.BacklinkInventory, format, Now);
        await using var output = new MemoryStream();
        var rendered = await ReportDocumentRenderer.RenderAsync(report, Summary, Rows(), output, TestContext.Current.CancellationToken);

        Assert.Equal(1, rendered.RowCount);
        Assert.NotEmpty(output.ToArray());
        output.Position = 0;

        switch (format)
        {
            case ReportFormat.Json:
                using (var json = await JsonDocument.ParseAsync(output, cancellationToken: TestContext.Current.CancellationToken))
                {
                    Assert.Equal(10, json.RootElement.GetProperty("summary").GetProperty("candidates").GetInt32());
                    Assert.Equal(12, json.RootElement.GetProperty("summary").GetProperty("attemptCount").GetInt32());
                    Assert.Equal("source.example", json.RootElement.GetProperty("summary").GetProperty("domainPerformance")[0].GetProperty("key").GetString());
                    Assert.Equal("=HYPERLINK(\"bad\")", json.RootElement.GetProperty("details")[0].GetProperty("anchor").GetString());
                }
                break;
            case ReportFormat.Csv:
                using (var reader = new StreamReader(output, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
                {
                    var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                    Assert.Contains("\"Candidates\"", csv, StringComparison.Ordinal);
                    Assert.Contains("\"Submission Success Rate %\"", csv, StringComparison.Ordinal);
                    Assert.Contains("\"Domain\",\"source.example\"", csv, StringComparison.Ordinal);
                    Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\"", csv, StringComparison.Ordinal);
                }
                break;
            case ReportFormat.Xlsx:
                using (var zip = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true))
                {
                    Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                    Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
                    Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
                    var detail = Assert.IsType<ZipArchiveEntry>(zip.GetEntry("xl/worksheets/sheet3.xml"));
                    await using var detailStream = detail.Open();
                    var document = await XDocument.LoadAsync(detailStream, LoadOptions.None, TestContext.Current.CancellationToken);
                    XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    Assert.Equal("A1:W2", document.Root?.Element(spreadsheet + "autoFilter")?.Attribute("ref")?.Value);
                }
                break;
            case ReportFormat.Html:
                using (var reader = new StreamReader(output, Encoding.UTF8, leaveOpen: true))
                {
                    var html = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                    Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
                    Assert.Contains("Performance by domain, template, and identity", html, StringComparison.Ordinal);
                    Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
                    Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
                }
                break;
        }
    }

    [Fact]
    public void CampaignReport_RequiresCampaignAndEnforcesLifecycle()
    {
        Assert.Throws<DomainRuleException>(() => new Report(Guid.CreateVersion7(Now), null, ReportKind.CampaignPerformance, ReportFormat.Json, Now));
        var report = new Report(Guid.CreateVersion7(Now), Guid.CreateVersion7(Now), ReportKind.CampaignPerformance, ReportFormat.Json, Now);
        var jobId = Guid.CreateVersion7(Now);
        report.AttachJob(jobId, Now);
        report.Start(Now);
        report.Complete("report.json", "application/json", 20, new string('a', 64), 1, Now);
        Assert.Equal(ReportStatus.Completed, report.Status);
        Assert.Equal(jobId, report.JobId);
    }

    private static async IAsyncEnumerable<ReportRowDto> Rows()
    {
        yield return new ReportRowDto("https://source.example/<script>", "https://target.example/", "=HYPERLINK(\"bad\")", OpportunityType.ResourcePage, SubmissionStatus.Submitted, BacklinkStatus.Verified, ["nofollow", "ugc"], 200, Now, Now, Now, Now, null);
        await Task.CompletedTask;
    }
}
