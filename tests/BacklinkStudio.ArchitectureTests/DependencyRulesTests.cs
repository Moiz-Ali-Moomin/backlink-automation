using System.Reflection;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_HasNoFrameworkOrInfrastructureDependencies()
    {
        var references = typeof(Project).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || x.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || x.StartsWith("Npgsql", StringComparison.Ordinal) || x.Contains("Infrastructure", StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_DoesNotDependOnHostsOrInfrastructure()
    {
        var references = typeof(IProjectService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal)));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnFeatureOrHostProjects()
    {
        var references = typeof(BacklinkStudio.Infrastructure.DependencyInjection).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Opportunities", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Fact]
    public void DiscoveryFeature_DoesNotDependOnInfrastructureOrHosts()
    {
        var references = typeof(BacklinkStudio.Discovery.DiscoveryService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Fact]
    public void SubmissionFeature_DoesNotDependOnInfrastructureOrHosts()
    {
        var references = typeof(BacklinkStudio.Submission.CampaignService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Fact]
    public void VerificationFeature_DoesNotDependOnInfrastructureOrHosts()
    {
        var references = typeof(BacklinkStudio.Verification.VerificationService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Fact]
    public void SchedulingFeature_DoesNotDependOnInfrastructureOrHosts()
    {
        var references = typeof(BacklinkStudio.Scheduling.ScheduleService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReportingFeature_DoesNotDependOnInfrastructureOrHosts()
    {
        var references = typeof(BacklinkStudio.Reporting.ReportService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Infrastructure", StringComparison.Ordinal) || x.EndsWith(".Api", StringComparison.Ordinal) || x.EndsWith(".Mcp", StringComparison.Ordinal) || x.EndsWith(".Worker", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("BacklinkStudio.Api")]
    [InlineData("BacklinkStudio.Mcp")]
    [InlineData("BacklinkStudio.Worker")]
    public void Hosts_DoNotUseDbContextDirectly(string project)
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories);
        Assert.DoesNotContain(files, file => File.ReadAllText(file).Contains("BacklinkStudioDbContext", StringComparison.Ordinal));
    }

    [Fact]
    public void Worker_UsesApplicationJobContracts()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BacklinkStudio.Worker", "PersistentJobWorker.cs"));
        Assert.Contains("IJobQueue", source, StringComparison.Ordinal);
        Assert.Contains("IJobExecutor", source, StringComparison.Ordinal);
        Assert.Contains("Channel.CreateBounded", source, StringComparison.Ordinal);
        Assert.Contains("IWorkerRegistry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduler_UsesApplicationScheduleContracts()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BacklinkStudio.Worker", "PersistentScheduler.cs"));
        Assert.Contains("IScheduleStore", source, StringComparison.Ordinal);
        Assert.Contains("IScheduleRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BacklinkStudioDbContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void McpTransport_DoesNotExposeLowLevelNetworkExecution()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BacklinkStudio.Mcp", "Protocol", "McpToolDispatcher.cs"));
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISiteAnalyzer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBacklinkVerifier", source, StringComparison.Ordinal);
        Assert.DoesNotContain("submit_arbitrary_form", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http_request_any_url", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlledBrowserAdapters_DoNotBufferTheRenderedDocumentOrEvaluateArbitraryScript()
    {
        var root = RepositoryRoot();
        foreach (var file in new[] { "ControlledBrowserValidationAdapter.cs", "ControlledBrowserCommentAdapter.cs",
                     "ControlledBrowserBacklinkVerifier.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "BacklinkStudio.Infrastructure", "Http", file));
            Assert.DoesNotContain("ContentAsync(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InnerTextAsync(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EvaluateAsync(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ControlledBrowserCommentAdapter_ClicksOnlyAVisibleBoundedSubmitControl()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BacklinkStudio.Infrastructure",
            "Http", "ControlledBrowserCommentAdapter.cs"));
        Assert.Contains("VisibleSubmitControlSelector", source, StringComparison.Ordinal);
        Assert.Contains(":visible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PressAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchEventAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubActions_ArePinnedToImmutableCommits()
    {
        var workflows = Directory.GetFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml");
        var actionLines = workflows.SelectMany(File.ReadLines).Select(x => x.Trim()).Where(x => x.StartsWith("- uses:", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(actionLines);
        Assert.All(actionLines, line =>
        {
            var reference = line.Split('@', 2).ElementAtOrDefault(1)?.Split(' ', 2)[0];
            Assert.Matches("^[0-9a-f]{40}$", reference ?? string.Empty);
        });
    }

    [Fact]
    public void BackupRestoreScripts_RequireIntegrityAndExplicitDestructiveConfirmation()
    {
        var root = RepositoryRoot();
        var backup = File.ReadAllText(Path.Combine(root, "scripts", "postgres-backup.sh"));
        var restore = File.ReadAllText(Path.Combine(root, "scripts", "postgres-restore.sh"));
        Assert.Contains("sha256sum", backup, StringComparison.Ordinal);
        Assert.Contains("pg_restore --list", backup, StringComparison.Ordinal);
        Assert.Contains("BACKLINKSTUDIO_RESTORE_CONFIRM", restore, StringComparison.Ordinal);
        Assert.Contains("actual_checksum=$(sha256sum -- \"$archive\"", restore, StringComparison.Ordinal);
        Assert.Contains("actual_checksum\" != \"$expected_checksum", restore, StringComparison.Ordinal);
        Assert.Contains("--single-transaction", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerAndObservabilityConfiguration_PreserveProductionBoundaries()
    {
        var root = RepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        Assert.Contains("sdk:10.0.400", dockerfile, StringComparison.Ordinal);
        Assert.Contains("aspnet:10.0.11-noble-chiseled-extra", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
        Assert.Contains("cap_drop: [ALL]", compose, StringComparison.Ordinal);
        Assert.Contains("otel/opentelemetry-collector-contrib:0.159.0", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:${OTEL_METRICS_BIND_PORT:-9464}:9464", compose, StringComparison.Ordinal);

        var postgresSection = compose[compose.IndexOf("  postgres:", StringComparison.Ordinal)..compose.IndexOf("  redis:", StringComparison.Ordinal)];
        var collectorSection = compose[compose.IndexOf("  otel-collector:", StringComparison.Ordinal)..compose.IndexOf("  backlinkstudio-migrate:", StringComparison.Ordinal)];
        Assert.Contains("networks: [backend]", postgresSection, StringComparison.Ordinal);
        Assert.DoesNotContain("edge", postgresSection, StringComparison.Ordinal);
        Assert.Contains("networks: [backend, edge]", collectorSection, StringComparison.Ordinal);
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
