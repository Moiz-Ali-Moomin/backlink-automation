namespace BacklinkStudio.ContractTests;

public sealed class RestContractTests
{
    [Fact]
    public void ApiSource_DeclaresVersionedBoundedAsyncContracts()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BacklinkStudio.Api", "ApiEndpoints.cs"));
        Assert.Contains("/api/v1", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId:guid}/candidates/import", source, StringComparison.Ordinal);
        Assert.Contains("/candidates/{id:guid}", source, StringComparison.Ordinal);
        Assert.Contains("/agent-credentials/{id:guid}/rotate", source, StringComparison.Ordinal);
        Assert.Contains("/agent-credentials/{id:guid}/revoke", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId:guid}/analysis-jobs", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId:guid}/policy", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId:guid}/blocklist", source, StringComparison.Ordinal);
        Assert.Contains("/discovery/jobs", source, StringComparison.Ordinal);
        Assert.Contains("/discovery/runs/{id:guid}", source, StringComparison.Ordinal);
        Assert.Contains("/jobs/{id:guid}/pause", source, StringComparison.Ordinal);
        Assert.Contains("/jobs/{id:guid}/resume", source, StringComparison.Ordinal);
        Assert.Contains("/jobs/{id:guid}/redrive", source, StringComparison.Ordinal);
        Assert.Contains("/verification/jobs", source, StringComparison.Ordinal);
        Assert.Contains("/campaigns/{id:guid}/pause", source, StringComparison.Ordinal);
        Assert.Contains("/campaigns/{id:guid}/resume", source, StringComparison.Ordinal);
        Assert.Contains("/campaigns/{id:guid}/stop", source, StringComparison.Ordinal);
        Assert.Contains("/backlinks/{id:guid}/verification-history", source, StringComparison.Ordinal);
        Assert.Contains("/schedules/{id:guid}/pause", source, StringComparison.Ordinal);
        Assert.Contains("/schedules/{id:guid}/resume", source, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/schedules/{id:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/reports\"", source, StringComparison.Ordinal);
        Assert.Contains("/reports/{id:guid}/download", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/backlink-workflows\"", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/backlink-workflows/{id:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("AuthorizationScopes.ReportsWrite", source, StringComparison.Ordinal);
        Assert.Contains("AuthorizationScopes.ReportsRead", source, StringComparison.Ordinal);
        Assert.Contains("Results.Accepted", source, StringComparison.Ordinal);
        Assert.Contains("PageRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BacklinkStudioDbContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Documentation_ListsExactHealthAndOpenApiRoutes()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "API.md"));
        Assert.Contains("/health/live", api, StringComparison.Ordinal);
        Assert.Contains("/health/ready", api, StringComparison.Ordinal);
        Assert.Contains("/openapi/v1.json", api, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerImage_PrecreatesNonRootReportStorage()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile"));
        Assert.Contains("/out/reports", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--chown=$APP_UID:$APP_UID /out/reports /var/lib/backlinkstudio/reports", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
