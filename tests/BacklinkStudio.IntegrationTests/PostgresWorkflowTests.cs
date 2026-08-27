using System.Net;
using System.Net.Sockets;
using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure;
using BacklinkStudio.Infrastructure.Persistence;
using BacklinkStudio.Infrastructure.Security;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Reporting;
using BacklinkStudio.Scheduling;
using BacklinkStudio.Submission;
using BacklinkStudio.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BacklinkStudio.IntegrationTests;

public sealed class PostgresWorkflowTests
{
    [Fact]
    public async Task BacklinkWorkflowStart_PersistsMixedBatchBeforeWorkerAuthorization()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1",
            "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        Guid projectId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var project = new Project("Simple mixed workflow", "target.example", null, now);
            var policy = new PolicyDefinition(project.Id, now);
            policy.Update(true, 0, 100, false, 1_000, 10_000, 1_000, now);
            var network = new OwnedNetworkProfile(project.Id, "Simple workflow network", null,
                OwnershipStatus.Owned, true, null, null, null, 8, 2, 0, true, now);
            var rule = new OwnedNetworkDomain(network.Id, "owned.example",
                OwnedNetworkDomainMatchType.ExactHost, true, now);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.AddRange(project, policy, network, rule);
            await db.SaveChangesAsync(cancellationToken);
            projectId = project.Id;
        }

        BacklinkWorkflowAcceptedDto accepted;
        await using (var scope = provider.CreateAsyncScope())
        {
            var command = new StartBacklinkWorkflowCommand(projectId,
                ["https://owned.example/post/", "https://not-authorized.example/post/"], null,
                [new("John", "john@example.com")], null, ["Useful article."], null,
                "https://target.example/", 4, 1, 0, 3, 0, "mixed-workflow-start");
            accepted = await scope.ServiceProvider.GetRequiredService<IBacklinkWorkflowService>()
                .StartAsync(command, new(ActorType.Agent, "integration", null, "mixed-workflow", "local"),
                    cancellationToken);
            Assert.Null(accepted.CampaignId);

            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var initial = await db.BacklinkWorkflowSources.AsNoTracking()
                .Where(value => value.WorkflowId == accepted.WorkflowId).ToArrayAsync(cancellationToken);
            Assert.Equal(2, initial.Length);
            Assert.All(initial, value => Assert.Equal(BacklinkWorkflowSourceStatus.Queued, value.Status));
            Assert.All(initial, value => Assert.Null(value.SubmissionSourceId));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            var executionNow = TimeProvider.System.GetUtcNow();
            var job = Assert.IsType<PersistentJob>(await queue.ClaimAsync("workflow-worker",
                TimeSpan.FromMinutes(5), ClaimLimits, executionNow, cancellationToken));
            Assert.Equal(accepted.JobId, job.Id);
            await queue.MarkRunningAsync(job.Id, "workflow-worker", TimeSpan.FromMinutes(5), executionNow,
                cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>()
                .Single(value => value.JobType == JobType.BacklinkWorkflow)
                .ExecuteAsync(job, "workflow-worker", cancellationToken);
            await queue.MarkSucceededAsync(job.Id, "workflow-worker", executionNow.AddSeconds(1),
                cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var rows = await db.BacklinkWorkflowSources.AsNoTracking()
                .Where(value => value.WorkflowId == accepted.WorkflowId)
                .OrderBy(value => value.Host).ToArrayAsync(cancellationToken);
            var authorized = Assert.Single(rows, value => value.Host == "owned.example");
            Assert.Equal(BacklinkWorkflowSourceStatus.Checking, authorized.Status);
            Assert.NotNull(authorized.SubmissionSourceId);
            Assert.NotNull(authorized.CampaignId);
            var unauthorized = Assert.Single(rows, value => value.Host == "not-authorized.example");
            Assert.Equal(BacklinkWorkflowSourceStatus.NotAuthorized, unauthorized.Status);
            Assert.Null(unauthorized.SubmissionSourceId);
            Assert.Single(await db.Jobs.AsNoTracking()
                .Where(value => value.Type == JobType.SubmissionSourceValidation &&
                                value.CorrelationId == accepted.WorkflowId.ToString("D"))
                .ToArrayAsync(cancellationToken));
        }
    }

    [Fact]
    public async Task InfrastructureComposition_RegistersMilestoneFourServices()
    {
        await using var provider = BuildProvider("Host=localhost;Database=backlinkstudio;Username=backlinkstudio;Password=not-used");
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IProjectService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IJobQueue>());
        Assert.NotNull(scope.ServiceProvider.GetService<IDatabaseInitializer>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISiteAnalyzer>());
        Assert.NotNull(scope.ServiceProvider.GetService<IPolicyEvaluator>());
        Assert.NotNull(scope.ServiceProvider.GetService<IDiscoveryService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IWorkerRegistry>());
        Assert.NotNull(scope.ServiceProvider.GetService<IOperationalMetricsReader>());
        Assert.NotNull(scope.ServiceProvider.GetService<IDomainRateLimiter>());
        Assert.NotNull(scope.ServiceProvider.GetService<IJobFailureClassifier>());
        Assert.NotNull(scope.ServiceProvider.GetService<ICampaignService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IVerificationService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IScheduleService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IScheduleStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<IScheduleRunner>());
        Assert.NotNull(scope.ServiceProvider.GetService<IAgentCredentialService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IReportService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IOwnedNetworkService>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISubmissionSourceService>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISubmissionIdentityService>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISubmissionTemplateService>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISubmissionPreviewService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IOwnedNetworkWorkflowService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IWordPressSiteProfileService>());
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.SubmissionSourceImport);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.SubmissionSourceValidation);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.OwnedNetworkWorkflow);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.OwnedNetworkCampaignExpansion);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.Submission);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.Verification);
        Assert.Contains(scope.ServiceProvider.GetServices<IJobExecutor>(), x => x.JobType == JobType.Report);
        Assert.Equal(6, scope.ServiceProvider.GetServices<IDiscoveryProvider>().Count());
    }

    [Fact]
    public async Task WorkflowSourceStatus_IsScopedToTheWorkflow_WhenCatalogSourceIsReused()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        Guid firstWorkflowId;
        Guid secondWorkflowId;
        Guid sourceId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var project = new Project("Workflow scope", "target.example", null, now);
            var network = new OwnedNetworkProfile(project.Id, "Workflow network", null, OwnershipStatus.Owned,
                true, null, null, null, 10, 2, 0, true, now);
            var source = new SubmissionSource(project.Id, network.Id, "https://blog.example/post/",
                "https://blog.example/post/", "blog.example", "blog.example", OwnershipStatus.Owned, true,
                "workflow-scope", true, now);
            var identityPool = new SubmissionIdentityPool(project.Id, "Workflow identities",
                PoolSelectionStrategy.RoundRobin, IdentityEmailStrategy.Fixed, null, null, true, now);
            var templatePool = new SubmissionTemplatePool(project.Id, "Workflow templates",
                SubmissionTemplateType.WordPressComment, PoolSelectionStrategy.RoundRobin,
                BacklinkPlacementMethod.WebsiteField, true, now);
            var firstWorkflow = new BacklinkWorkflow(project.Id, null, identityPool.Id, templatePool.Id,
                "https://target.example/", 1, 1, 0, 3, 0, now);
            var secondWorkflow = new BacklinkWorkflow(project.Id, null, identityPool.Id, templatePool.Id,
                "https://target.example/", 1, 1, 0, 3, 0, now.AddMilliseconds(1));
            firstWorkflow.MarkRunning(now);
            var firstWorkflowSource = new BacklinkWorkflowSource(firstWorkflow.Id, source.OriginalUrl,
                source.NormalizedUrl, source.Domain, source.Host, BacklinkWorkflowSourceStatus.Queued, null, now,
                source.Id);
            var secondWorkflowSource = new BacklinkWorkflowSource(secondWorkflow.Id, source.OriginalUrl,
                source.NormalizedUrl, source.Domain, source.Host, BacklinkWorkflowSourceStatus.Queued, null,
                now.AddMilliseconds(1), source.Id);

            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.AddRange(project, network, source, identityPool, templatePool, firstWorkflow, secondWorkflow,
                firstWorkflowSource, secondWorkflowSource);
            await db.SaveChangesAsync(cancellationToken);
            firstWorkflowId = firstWorkflow.Id;
            secondWorkflowId = secondWorkflow.Id;
            sourceId = source.Id;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBacklinkWorkflowRepository>();
            await repository.SetSourceStatusAsync(firstWorkflowId, sourceId, BacklinkWorkflowSourceStatus.Failed,
                "terminal failure", now.AddSeconds(1), cancellationToken);
            await repository.SettleAsync(firstWorkflowId, now.AddSeconds(1), cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var firstSource = await db.BacklinkWorkflowSources.AsNoTracking()
                .SingleAsync(x => x.WorkflowId == firstWorkflowId, cancellationToken);
            var secondSource = await db.BacklinkWorkflowSources.AsNoTracking()
                .SingleAsync(x => x.WorkflowId == secondWorkflowId, cancellationToken);
            var firstWorkflow = await db.BacklinkWorkflows.AsNoTracking()
                .SingleAsync(x => x.Id == firstWorkflowId, cancellationToken);
            var secondWorkflow = await db.BacklinkWorkflows.AsNoTracking()
                .SingleAsync(x => x.Id == secondWorkflowId, cancellationToken);

            Assert.Equal(BacklinkWorkflowSourceStatus.Failed, firstSource.Status);
            Assert.Equal(BacklinkWorkflowSourceStatus.Queued, secondSource.Status);
            Assert.Equal(BacklinkWorkflowStatus.CompletedWithFailures, firstWorkflow.Status);
            Assert.Equal(BacklinkWorkflowStatus.Queued, secondWorkflow.Status);
        }
    }

    [Fact]
    public async Task ReportJob_PersistsGeneratesAndDownloadsArtifact()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "report-agent", null, "report-request", "local");
        ReportAcceptedDto accepted;

        await using (var scope = provider.CreateAsyncScope())
        {
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(new("Report project", "report.example", null, "report-project"), actor, cancellationToken);
            var backlink = new Backlink(project.Id, null, null, "https://source.example/resources", "https://source.example/resources", "https://report.example/", "https://report.example/", "source.example", TimeProvider.System.GetUtcNow());
            backlink.ApplyVerification(true, 200, "Report link", ["nofollow"], null, null, TimeProvider.System.GetUtcNow());
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Backlinks.Add(backlink);
            await db.SaveChangesAsync(cancellationToken);
            var command = new GenerateReportCommand(project.Id, null, ReportKind.BacklinkInventory, ReportFormat.Json, "report-generate");
            accepted = await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(command, actor, cancellationToken);
            var replay = await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(command, actor, cancellationToken);
            Assert.Equal(accepted, replay);
        }

        PersistentJob claimed;
        await using (var scope = provider.CreateAsyncScope())
        {
            claimed = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("report-worker", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(claimed.Id, "report-worker", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Report).ExecuteAsync(claimed, "report-worker", cancellationToken);
            await queue.MarkSucceededAsync(claimed.Id, "report-worker", TimeProvider.System.GetUtcNow(), cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IReportService>();
            var report = Assert.IsType<ReportDto>(await service.GetAsync(accepted.ReportId, cancellationToken));
            Assert.Equal(ReportStatus.Completed, report.Status);
            Assert.Equal(1, report.RowCount);
            Assert.Equal(64, report.Sha256?.Length);
            var download = Assert.IsType<ReportDownload>(await service.DownloadAsync(report.Id, cancellationToken));
            await using (download.Content)
            {
                using var document = await System.Text.Json.JsonDocument.ParseAsync(download.Content, cancellationToken: cancellationToken);
                Assert.Equal(1, document.RootElement.GetProperty("details").GetArrayLength());
                Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("verified").GetInt32());
            }
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            Assert.Equal(2, await db.AuditEvents.CountAsync(x => x.Operation == "report.enqueue" || x.Operation == "report.complete", cancellationToken));
        }
    }

    [Fact]
    public async Task OwnedSourceStateFilters_BacklinkProvenanceAndPerformanceMetrics_ArePersistedAndQueryable()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        Guid projectId;
        Guid sourceId;
        Guid attemptId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var project = new Project("Owned report metrics", "target.example", null, now);
            var network = new OwnedNetworkProfile(project.Id, "Owned network", null, OwnershipStatus.Owned,
                true, null, null, null, 10, 2, 0, true, now);
            var source = new SubmissionSource(project.Id, network.Id, "https://blog.example/post/",
                "https://blog.example/post/", "blog.example", "blog.example", OwnershipStatus.Owned, true,
                "metrics", true, now);
            var untouchedSource = new SubmissionSource(project.Id, network.Id, "https://blog.example/other/",
                "https://blog.example/other/", "blog.example", "blog.example", OwnershipStatus.Owned, true,
                "metrics", true, now.AddMilliseconds(1));
            var identityPool = new SubmissionIdentityPool(project.Id, "Brand identities", PoolSelectionStrategy.RoundRobin,
                IdentityEmailStrategy.Fixed, null, null, true, now);
            var identity = new SubmissionIdentity(identityPool.Id, "Owned Brand", "brand@example.com",
                "https://target.example/", "Owned Brand", true, 1, now);
            var templatePool = new SubmissionTemplatePool(project.Id, "Brand comments", SubmissionTemplateType.WordPressComment,
                PoolSelectionStrategy.RoundRobin, BacklinkPlacementMethod.WebsiteField, true, now);
            var template = new SubmissionTemplate(templatePool.Id, "Brand comment", "Useful owned-network page.",
                null, null, null, null, true, 1, now);
            var campaign = new Campaign(project.Id, "Owned campaign", CampaignApprovalMode.Automatic,
                "owned-network", "integration", 100, now);
            var durableJob = new PersistentJob(JobType.Submission, project.Id, campaign.Id, "{\"version\":1}", 0,
                now, 3, "owned-metrics", "owned-metrics", now, source.Domain);
            var submission = new SubmissionJob(project.Id, campaign.Id, source.Id, durableJob.Id,
                "https://target.example/", BacklinkPlacementMethod.WebsiteField, now);
            submission.MarkSubmitted(false, now.AddSeconds(1));
            var attempt = new SubmissionAttempt(submission.Id, durableJob.Id, project.Id, campaign.Id, source.Id,
                "https://target.example/", 1, "OwnedWordPressCommentAdapter", "StandardComment", identity.Id,
                template.Id, identity.DisplayName, identity.Email, identity.Website, now);
            attempt.Complete(new OwnedWordPressSubmissionResultValue(SubmissionStatus.Submitted, ModerationStatus.Approved,
                "StandardComment", 302, "comment-1", SubmissionFailureKind.None, null), now.AddSeconds(1));
            var backlink = new Backlink(project.Id, campaign.Id, submission.Id, source.OriginalUrl, source.NormalizedUrl,
                "https://target.example/", "https://target.example/", source.Domain, now.AddSeconds(1), source.Id, attempt.Id);
            backlink.ApplyVerification(true, 200, "Owned Brand", ["ugc"], source.NormalizedUrl, null, now.AddSeconds(2));

            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.AddRange(project, network, source, untouchedSource, identityPool, identity, templatePool, template,
                campaign, durableJob, submission, attempt, backlink);
            await db.SaveChangesAsync(cancellationToken);
            projectId = project.Id;
            sourceId = source.Id;
            attemptId = attempt.Id;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var sources = scope.ServiceProvider.GetRequiredService<ISubmissionSourceRepository>();
            var bySubmission = await sources.ListAsync(projectId,
                new SubmissionSourceFilter(PreviousSubmissionStatus: SubmissionStatus.Submitted), null, 10, cancellationToken);
            Assert.Equal(sourceId, Assert.Single(bySubmission).Id);
            var byVerification = await sources.ListAsync(projectId,
                new SubmissionSourceFilter(PreviousVerificationStatus: BacklinkStatus.Verified), null, 10, cancellationToken);
            Assert.Equal(sourceId, Assert.Single(byVerification).Id);

            var backlink = Assert.Single(await scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>()
                .Backlinks.AsNoTracking().ToListAsync(cancellationToken));
            Assert.Equal(sourceId, backlink.SubmissionSourceId);
            Assert.Equal(attemptId, backlink.SubmissionAttemptId);

            var summary = await scope.ServiceProvider.GetRequiredService<IReportDataSource>()
                .GetSummaryAsync(projectId, null, ReportKind.BacklinkInventory, cancellationToken);
            Assert.Equal(1, summary.AttemptCount);
            Assert.Equal(100, summary.SubmissionSuccessRate);
            Assert.Equal(100, summary.VerificationRate);
            Assert.Equal("blog.example", Assert.Single(summary.DomainPerformance!).Key);
            Assert.Equal("Brand comment", Assert.Single(summary.TemplatePerformance!).Key);
            Assert.Equal("Owned Brand", Assert.Single(summary.IdentityPerformance!).Key);
        }
    }

    [Fact]
    public async Task RecoveredOwnedSubmission_WithUnfinishedRemoteAttempt_ReconcilesWithoutSendingAgain()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        Guid submissionId;
        Guid attemptId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var project = new Project("Crash reconciliation", "target.example", null, now);
            var policy = new PolicyDefinition(project.Id, now);
            policy.Update(true, 100, 100, false, 1_000, 10_000, 1_000, now);
            var network = new OwnedNetworkProfile(project.Id, "Controlled WordPress", null, OwnershipStatus.Owned,
                true, null, null, null, 10, 2, 0, true, now);
            var networkDomain = new OwnedNetworkDomain(network.Id, "owned.example", OwnedNetworkDomainMatchType.ExactHost, true, now);
            var source = new SubmissionSource(project.Id, network.Id, "https://owned.example/post/",
                "https://owned.example/post/", "owned.example", "owned.example", OwnershipStatus.Owned, true,
                null, true, now);
            source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress, OpportunityType.WordPressComment,
                nameof(OwnedWordPressCommentAdapter), TechnicalCompatibility.Compatible, SubmissionSourceValidationStatus.Valid,
                "compatible", "controlled WordPress comment form", false, false, false, true, false, false, 42,
                "https://owned.example/wp-comments-post.php", "https://owned.example/wp-comments-post.php", "Owned post",
                source.NormalizedUrl, null, 200, "text/html", 512, [source.NormalizedUrl], true, "author", "email", "url",
                "comment", "comment_post_ID", [], false, false, null), now);
            var identityPool = new SubmissionIdentityPool(project.Id, "Identities", PoolSelectionStrategy.RoundRobin,
                IdentityEmailStrategy.Fixed, null, null, true, now);
            var identity = new SubmissionIdentity(identityPool.Id, "Owned Brand", "owned@example.com",
                "https://target.example/", null, true, 1, now);
            var templatePool = new SubmissionTemplatePool(project.Id, "Templates", SubmissionTemplateType.WordPressComment,
                PoolSelectionStrategy.RoundRobin, BacklinkPlacementMethod.WebsiteField, true, now);
            var template = new SubmissionTemplate(templatePool.Id, "Comment", "Controlled reconciliation comment.",
                null, null, null, null, true, 1, now);
            var campaign = new Campaign(project.Id, "Crash-window campaign", CampaignApprovalMode.Automatic,
                "owned-network", "controlled-crash-test", 1_000, now);
            campaign.Start(now);
            var configuration = new OwnedNetworkCampaignConfiguration(campaign.Id, project.Id, network.Id,
                "https://target.example/", identityPool.Id, templatePool.Id, 10, 2, 0, 3, 0,
                OwnedNetworkCampaignMode.AutomaticOwnedNetwork, null, null, null, TechnicalCompatibility.Compatible,
                SubmissionSourceValidationStatus.Valid, null, now);
            var payload = new OwnedNetworkSubmissionJobPayload(2, campaign.Id, source.Id, identityPool.Id,
                templatePool.Id, "https://target.example/", BacklinkPlacementMethod.WebsiteField, 0);
            var job = new PersistentJob(JobType.Submission, project.Id, campaign.Id,
                System.Text.Json.JsonSerializer.Serialize(payload), 0, now, 3, "crash-reconcile", "crash-reconcile", now,
                source.Domain);
            job.Claim("crashed-worker", now, TimeSpan.FromSeconds(1));
            job.Start("crashed-worker", now, TimeSpan.FromSeconds(1));
            var submission = new SubmissionJob(project.Id, campaign.Id, source.Id, job.Id,
                "https://target.example/", BacklinkPlacementMethod.WebsiteField, now);
            submission.MarkProcessing(now);
            var unfinished = new SubmissionAttempt(submission.Id, job.Id, project.Id, campaign.Id, source.Id,
                "https://target.example/", 1, nameof(OwnedWordPressCommentAdapter), "PendingSelection", identity.Id,
                template.Id, identity.DisplayName, identity.Email, identity.Website, now);
            job.Claim("replacement-worker", now.AddSeconds(2), TimeSpan.FromMinutes(1));
            job.Start("replacement-worker", now.AddSeconds(2), TimeSpan.FromMinutes(1));

            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.AddRange(project, policy, network, networkDomain, source, identityPool, identity, templatePool, template,
                campaign, configuration, job, submission, unfinished);
            await db.SaveChangesAsync(cancellationToken);
            submissionId = submission.Id;
            attemptId = unfinished.Id;

            var executor = scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Submission);
            await executor.ExecuteAsync(job, "replacement-worker", cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var submission = await db.SubmissionJobs.AsNoTracking().SingleAsync(x => x.Id == submissionId, cancellationToken);
            Assert.Equal(SubmissionStatus.ReconciliationRequired, submission.Status);
            var persistedAttempt = await db.SubmissionAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId, cancellationToken);
            Assert.Null(persistedAttempt.FinishedAt);
            var backlink = await db.Backlinks.AsNoTracking().SingleAsync(x => x.SubmissionJobId == submissionId, cancellationToken);
            Assert.Equal(BacklinkStatus.PendingVerification, backlink.Status);
            Assert.Equal(attemptId, backlink.SubmissionAttemptId);
            Assert.Single(await db.Jobs.AsNoTracking().Where(x => x.Type == JobType.Verification &&
                x.IdempotencyKey == $"backlink:{backlink.Id}:reconcile").ToListAsync(cancellationToken));
            Assert.Single(await db.AuditEvents.AsNoTracking().Where(x => x.Operation == "submission.reconciliation_required" &&
                x.ActorId == "replacement-worker").ToListAsync(cancellationToken));
            Assert.Equal(1, await db.SubmissionAttempts.CountAsync(x => x.SubmissionJobId == submissionId, cancellationToken));
        }
    }

    [Fact]
    public async Task CampaignReport_StreamsCampaignStatisticsAndDetailFromPostgres()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "campaign-report-agent", null, "campaign-report-request", "local");
        ReportAcceptedDto accepted;

        await using (var scope = provider.CreateAsyncScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(new("Campaign report project", "campaign-report.example", null, "campaign-report-project"), actor, cancellationToken);
            var target = await projects.AddTargetAsync(new(project.Id, "https://campaign-report.example/product", "Product", null, null, null, 50, "campaign-report-target"), actor, cancellationToken);
            var now = TimeProvider.System.GetUtcNow();
            var site = new CandidateSite(project.Id, "partner.example", now);
            var page = new CandidatePage(project.Id, site.Id, "https://partner.example/resource", "https://partner.example/resource", now);
            var opportunity = new Opportunity(project.Id, page.Id, page.Url, site.Domain, "Eligible resource", now);
            opportunity.ApplyAnalysis(OpportunityType.ResourcePage, 80, 10, AutomationStatus.ApprovalRequired, "Eligible resource", [], now);
            opportunity.Approve(actor.ActorId, now);
            var campaign = new Campaign(project.Id, "Campaign report", CampaignApprovalMode.Manual, "owned", "integration", 10, now);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.CandidateSites.Add(site);
            db.CandidatePages.Add(page);
            db.Opportunities.Add(opportunity);
            db.Campaigns.Add(campaign);
            db.CampaignTargets.Add(new CampaignTarget(campaign.Id, target.Id, now));
            db.CampaignOpportunities.Add(new CampaignOpportunity(campaign.Id, opportunity.Id, true, now));
            await db.SaveChangesAsync(cancellationToken);
            accepted = await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(new(project.Id, campaign.Id, ReportKind.CampaignPerformance, ReportFormat.Json, "campaign-report-generate"), actor, cancellationToken);
        }

        PersistentJob claimed;
        await using (var scope = provider.CreateAsyncScope())
        {
            claimed = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("campaign-report-worker", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(claimed.Id, "campaign-report-worker", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Report).ExecuteAsync(claimed, "campaign-report-worker", cancellationToken);
            await queue.MarkSucceededAsync(claimed.Id, "campaign-report-worker", TimeProvider.System.GetUtcNow(), cancellationToken);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var download = Assert.IsType<ReportDownload>(await scope.ServiceProvider.GetRequiredService<IReportService>().DownloadAsync(accepted.ReportId, cancellationToken));
            await using (download.Content)
            {
                using var document = await System.Text.Json.JsonDocument.ParseAsync(download.Content, cancellationToken: cancellationToken);
                Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("candidates").GetInt32());
                Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("approved").GetInt32());
                Assert.Equal("resourcePage", document.RootElement.GetProperty("details")[0].GetProperty("opportunityType").GetString());
            }
        }
    }

    [Fact]
    public async Task AgentCredentials_DisplayOnceRotateRevokeAndAuthenticateByScope()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "admin-agent", null, "credential-request", "local");
        Guid firstId;
        string firstKey;

        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAgentCredentialService>();
            var command = new CreateAgentCredentialCommand("Read-only planner", [AuthorizationScopes.ProjectsRead, AuthorizationScopes.OpportunitiesRead], DateTimeOffset.UtcNow.AddDays(30), "credential-create");
            var created = await service.CreateAsync(command, actor, cancellationToken);
            firstId = created.Credential.Id;
            firstKey = Assert.IsType<string>(created.ApiKey);
            Assert.True(created.Displayed);
            var replay = await service.CreateAsync(command, actor, cancellationToken);
            Assert.Equal(firstId, replay.Credential.Id);
            Assert.False(replay.Displayed);
            Assert.Null(replay.ApiKey);
            var listed = await service.ListAsync(new PageRequest(10), cancellationToken);
            Assert.Contains(listed.Items, x => x.Id == firstId && x.Scopes.SequenceEqual(new[] { AuthorizationScopes.OpportunitiesRead, AuthorizationScopes.ProjectsRead }));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var authenticated = Assert.IsType<AuthenticatedCredential>(await scope.ServiceProvider.GetRequiredService<ICredentialAuthenticator>().AuthenticateAsync(firstKey, cancellationToken));
            Assert.DoesNotContain(AuthorizationScopes.Admin, authenticated.Scopes);
        }

        Guid replacementId;
        string replacementKey;
        await using (var scope = provider.CreateAsyncScope())
        {
            var rotated = await scope.ServiceProvider.GetRequiredService<IAgentCredentialService>().RotateAsync(new(firstId, DateTimeOffset.UtcNow.AddDays(60), "credential-rotate"), actor, cancellationToken);
            replacementId = rotated.Credential.Id;
            replacementKey = Assert.IsType<string>(rotated.ApiKey);
            Assert.NotEqual(firstId, replacementId);
            Assert.Equal(rotated.Credential.UserId, (await scope.ServiceProvider.GetRequiredService<IAgentCredentialService>().GetAsync(firstId, cancellationToken))?.UserId);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var authenticator = scope.ServiceProvider.GetRequiredService<ICredentialAuthenticator>();
            Assert.Null(await authenticator.AuthenticateAsync(firstKey, cancellationToken));
            Assert.NotNull(await authenticator.AuthenticateAsync(replacementKey, cancellationToken));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAgentCredentialService>().RevokeAsync(new(replacementId, "credential-revoke"), actor, cancellationToken);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ICredentialAuthenticator>().AuthenticateAsync(replacementKey, cancellationToken));
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var idempotencyResponses = await db.IdempotencyRecords.AsNoTracking().Select(x => x.ResponseJson).ToListAsync(cancellationToken);
            Assert.DoesNotContain(idempotencyResponses, x => x.Contains(firstKey, StringComparison.Ordinal) || x.Contains(replacementKey, StringComparison.Ordinal));
            Assert.Equal(3, await db.AuditEvents.CountAsync(x => x.Operation == "credential.create" || x.Operation == "credential.rotate" || x.Operation == "credential.revoke", cancellationToken));
        }
    }

    [Fact]
    public async Task PersistentSchedule_RecoversExpiredClaimAndQueuesOneIdempotentOccurrence()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "schedule-agent", null, "schedule-request", "local");
        ScheduleDto created;

        await using (var scope = provider.CreateAsyncScope())
        {
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(new("Scheduled project", "schedule.example", null, "schedule-project"), actor, cancellationToken);
            var command = new CreateScheduleCommand(
                project.Id,
                "Analyze once",
                new ScheduleActionConfiguration(ScheduleActionType.Analysis, null, null),
                new ScheduleTiming(ScheduleRecurrenceType.OneTime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(1), null, null, null, null, null),
                "schedule-create");
            var service = scope.ServiceProvider.GetRequiredService<IScheduleService>();
            created = await service.CreateAsync(command, actor, cancellationToken);
            var replay = await service.CreateAsync(command, actor, cancellationToken);
            Assert.Equal(created.Id, replay.Id);
        }

        BacklinkStudio.Domain.Schedule firstClaim;
        await using (var scope = provider.CreateAsyncScope())
        {
            firstClaim = Assert.IsType<BacklinkStudio.Domain.Schedule>(await scope.ServiceProvider.GetRequiredService<IScheduleStore>().ClaimDueAsync("scheduler-a", TimeSpan.FromSeconds(1), created.NextRunAt, cancellationToken));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IScheduleStore>().ClaimDueAsync("scheduler-other", TimeSpan.FromMinutes(1), created.NextRunAt.AddMilliseconds(500), cancellationToken));
        }

        BacklinkStudio.Domain.Schedule recovered;
        await using (var scope = provider.CreateAsyncScope())
        {
            recovered = Assert.IsType<BacklinkStudio.Domain.Schedule>(await scope.ServiceProvider.GetRequiredService<IScheduleStore>().ClaimDueAsync("scheduler-b", TimeSpan.FromMinutes(5), created.NextRunAt.AddSeconds(2), cancellationToken));
            Assert.Equal(firstClaim.Id, recovered.Id);
            Assert.Equal(1, recovered.RecoveryCount);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IScheduleRunner>().RunAsync(recovered, "scheduler-b", cancellationToken);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var schedule = Assert.IsType<ScheduleDto>(await scope.ServiceProvider.GetRequiredService<IScheduleService>().GetAsync(created.Id, cancellationToken));
            Assert.Equal(ScheduleStatus.Completed, schedule.Status);
            Assert.Equal(1, schedule.LastJobCount);
            var jobs = await scope.ServiceProvider.GetRequiredService<IJobService>().ListAsync(schedule.ProjectId, new PageRequest(), cancellationToken);
            Assert.Single(jobs.Items);
            Assert.Equal(JobType.Analysis, jobs.Items[0].Type);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Operation == "schedule.occurrence", cancellationToken));
        }
    }

    [Fact]
    public async Task PermittedSubmission_CreatesPendingBacklink_AndVerificationPersistsEvidence()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var provider = BuildProvider(postgres.GetConnectionString(), port);
        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "submission-agent", null, "submission-request", "local");
        Guid persistentJobId;
        Guid campaignId;
        Guid backlinkId;
        Guid verificationJobId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var project = await projectService.CreateAsync(new("Owned submission", "target.example", null, "submission-project"), actor, cancellationToken);
            var target = await projectService.AddTargetAsync(new(project.Id, "https://target.example/product", "Product", null, "Owned anchor", null, 50, "submission-target"), actor, cancellationToken);
            var now = TimeProvider.System.GetUtcNow();
            var site = new CandidateSite(project.Id, "127.0.0.1", now);
            var page = new CandidatePage(project.Id, site.Id, $"http://127.0.0.1:{port}/source", $"http://127.0.0.1:{port}/source", now);
            var opportunity = new Opportunity(project.Id, page.Id, page.NormalizedUrl, site.Domain, "Owned endpoint", now);
            opportunity.ApplyAnalysis(OpportunityType.OwnedProperty, 90, 5, AutomationStatus.ApprovalRequired, "Explicitly owned", [], now);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.CandidateSites.Add(site);
            db.CandidatePages.Add(page);
            db.Opportunities.Add(opportunity);
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IOpportunityService>().ApproveAsync(new(project.Id, [opportunity.Id], "approve-owned"), actor, cancellationToken);
            var campaignService = scope.ServiceProvider.GetRequiredService<ICampaignService>();
            var campaign = await campaignService.CreateAsync(new(project.Id, "Owned test campaign", CampaignApprovalMode.Manual, "local-owned", "integration-test-authorization", 1, target.Id, [opportunity.Id], "create-owned-campaign"), actor, cancellationToken);
            campaignId = campaign.Id;
            var started = await campaignService.StartAsync(new(campaign.Id, "start-owned-campaign"), actor, cancellationToken);
            persistentJobId = Assert.Single(started.JobIds);
            var paused = await campaignService.PauseAsync(new(campaign.Id, "pause-owned-campaign"), actor, cancellationToken);
            Assert.Equal(CampaignStatus.Paused, paused.Status);
            Assert.Equal(JobStatus.Paused, (await scope.ServiceProvider.GetRequiredService<IJobService>().GetAsync(persistentJobId, cancellationToken))?.Status);
            var resumed = await campaignService.ResumeAsync(new(campaign.Id, "resume-owned-campaign"), actor, cancellationToken);
            Assert.Equal(CampaignStatus.Running, resumed.Status);
            Assert.Equal(JobStatus.RetryScheduled, (await scope.ServiceProvider.GetRequiredService<IJobService>().GetAsync(persistentJobId, cancellationToken))?.Status);
        }

        var server = AcceptSubmissionAsync(listener, cancellationToken);
        PersistentJob claimed;
        await using (var scope = provider.CreateAsyncScope())
        {
            claimed = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("submission-worker", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(persistentJobId, "submission-worker", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Submission).ExecuteAsync(claimed, "submission-worker", cancellationToken);
            await queue.MarkSucceededAsync(persistentJobId, "submission-worker", TimeProvider.System.GetUtcNow(), cancellationToken);
        }
        var capturedKey = await server.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICampaignService>();
            var submission = Assert.Single((await service.ListSubmissionsAsync(campaignId, new PageRequest(), cancellationToken)).Items);
            Assert.Equal(SubmissionStatus.Submitted, submission.Status);
            var attempt = Assert.Single(await service.ListAttemptsAsync(submission.Id, cancellationToken));
            Assert.Equal(SubmissionStatus.Submitted, attempt.Result);
            Assert.Equal(201, attempt.HttpStatus);
            Assert.Equal(submission.Id.ToString("N"), capturedKey);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var backlink = Assert.Single(await db.Backlinks.AsNoTracking().ToListAsync(cancellationToken));
            backlinkId = backlink.Id;
            Assert.Equal(BacklinkStatus.PendingVerification, backlink.Status);
            Assert.Null(backlink.FirstSeenAt);
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Operation == "submission.complete", cancellationToken));
            var accepted = await scope.ServiceProvider.GetRequiredService<IVerificationService>().StartAsync(new(backlink.Id, "verify-owned"), actor, cancellationToken);
            verificationJobId = accepted.JobId;
        }

        PersistentJob verificationJob;
        await using (var scope = provider.CreateAsyncScope())
        {
            verificationJob = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("verification-worker", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken));
            Assert.Equal(verificationJobId, verificationJob.Id);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(verificationJobId, "verification-worker", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Verification).ExecuteAsync(verificationJob, "verification-worker", cancellationToken);
            await queue.MarkSucceededAsync(verificationJobId, "verification-worker", TimeProvider.System.GetUtcNow(), cancellationToken);
            var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();
            var verified = Assert.IsType<BacklinkDto>(await service.GetAsync(backlinkId, cancellationToken));
            Assert.Equal(BacklinkStatus.Verified, verified.Status);
            Assert.Equal("Owned anchor", verified.AnchorText);
            Assert.True(verified.Nofollow);
            var check = Assert.Single((await service.HistoryAsync(backlinkId, new PageRequest(), cancellationToken)).Items);
            Assert.True(check.Found);
            Assert.Equal(200, check.HttpStatus);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Operation == "verification.complete", cancellationToken));
            var mutation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_checks SET found = false WHERE backlink_id = {backlinkId}", cancellationToken));
            Assert.Equal("55000", mutation.SqlState);
            var parentDeletion = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM backlinks WHERE id = {backlinkId}", cancellationToken));
            Assert.Equal("23503", parentDeletion.SqlState);
        }
    }

    [Fact]
    public async Task CleanDatabase_ExecutesCompleteMilestoneTwoWorkflow()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());

        await MigrateAndSeedAsync(provider, cancellationToken);
        var actor = new ActorContext(ActorType.Agent, "integration-agent", null, "integration-request", "local");
        Guid projectId;
        Guid jobId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(new CreateProjectCommand("Acme SEO", "Example.COM", "Integration project", "create-project-1"), actor, cancellationToken);
            projectId = project.Id;
            await scope.ServiceProvider.GetRequiredService<IProjectService>().AddTargetAsync(new AddTargetCommand(projectId, "https://example.com/product#details", "Product", ["widgets"], "Acme widgets", "product", 80, "add-target-1"), actor, cancellationToken);
            var policyService = scope.ServiceProvider.GetRequiredService<IPolicyService>();
            var blockCommand = new AddBlocklistEntryCommand(projectId, BlocklistMatchType.Domain, "blocked.example", "Integration block", "blocklist-1");
            var block = await policyService.AddBlocklistAsync(blockCommand, actor, cancellationToken);
            var replayedBlock = await policyService.AddBlocklistAsync(blockCommand, actor, cancellationToken);
            Assert.Equal(block.Id, replayedBlock.Id);
            var imported = await scope.ServiceProvider.GetRequiredService<ICandidateService>().ImportAsync(new ImportCandidatesCommand(projectId, ["https://partner.example/resources", "https://partner.example/resources/", "https://blocked.example/list", "not-a-url"], "import-1"), actor, cancellationToken);
            Assert.Equal(2, imported.Accepted);
            Assert.Equal(1, imported.Duplicates);
            Assert.Equal(1, imported.Invalid);
            var accepted = await scope.ServiceProvider.GetRequiredService<IOpportunityService>().StartAnalysisAsync(new StartAnalysisCommand(projectId, "analysis-1"), actor, cancellationToken);
            jobId = accepted.JobId;
        }

        PersistentJob claimed;
        await using (var scope = provider.CreateAsyncScope())
        {
            var claimedJob = await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("worker-1", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken);
            Assert.NotNull(claimedJob);
            claimed = claimedJob;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(jobId, "worker-1", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Analysis).ExecuteAsync(claimed, "worker-1", cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await queue.MarkSucceededAsync(jobId, "worker-1", TimeProvider.System.GetUtcNow(), cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<IJobService>().GetAsync(jobId, cancellationToken);
            Assert.Equal(JobStatus.Succeeded, job?.Status);
            var opportunities = await scope.ServiceProvider.GetRequiredService<IOpportunityService>().ListAsync(projectId, null, null, new PageRequest(), cancellationToken);
            var opportunity = Assert.Single(opportunities.Items);
            Assert.Equal(OpportunityType.ResourcePage, opportunity.Type);
            Assert.Equal(AutomationStatus.ManualActionRequired, opportunity.AutomationStatus);
            Assert.NotEmpty(opportunity.ScoreReasons);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var statuses = await db.CandidatePages.Select(x => x.AnalysisStatus).ToListAsync(cancellationToken);
            Assert.Contains(CandidateAnalysisStatus.Analyzed, statuses);
            Assert.Contains(CandidateAnalysisStatus.Blocked, statuses);
            Assert.Equal(1, await db.PolicyDefinitions.CountAsync(cancellationToken));
            Assert.Equal(1, await db.BlocklistEntries.CountAsync(cancellationToken));
            Assert.True(await db.AuditEvents.CountAsync(cancellationToken) >= 7);
            Assert.True(await db.AgentCredentials.AllAsync(x => x.KeyHash.Length == 64 && x.KeySalt.Length == 32, cancellationToken));
        }
    }

    [Fact]
    public async Task CleanDatabase_ExecutesAuditableIdempotentDiscoveryJob()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);

        var actor = new ActorContext(ActorType.Agent, "discovery-agent", null, "discovery-request", "local");
        DiscoveryAcceptedDto accepted;
        await using (var scope = provider.CreateAsyncScope())
        {
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(new CreateProjectCommand("Discovery", "example.com", null, "discovery-project"), actor, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IPolicyService>().AddBlocklistAsync(new AddBlocklistEntryCommand(project.Id, BlocklistMatchType.Domain, "blocked.example", "Blocked in integration", "discovery-block"), actor, cancellationToken);
            var command = new StartDiscoveryCommand(project.Id, DiscoveryProviderKind.ManualUrl, null, null,
                ["https://source.example/page", "https://source.example/page/", "https://blocked.example/list", "not-a-url"], 100, "discovery-run-1");
            var service = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();
            accepted = await service.StartAsync(command, actor, cancellationToken);
            var replay = await service.StartAsync(command, actor, cancellationToken);
            Assert.Equal(accepted, replay);
            await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.StartAsync(command with { Urls = ["https://different.example"], IdempotencyKey = "discovery-run-1" }, actor, cancellationToken));
            await Assert.ThrowsAsync<ValidationException>(() => service.StartAsync(command with { Urls = [null!], IdempotencyKey = "discovery-null-url" }, actor, cancellationToken));
        }

        PersistentJob job;
        await using (var scope = provider.CreateAsyncScope())
        {
            job = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("discovery-worker", TimeSpan.FromMinutes(5), ClaimLimits, TimeProvider.System.GetUtcNow(), cancellationToken));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(job.Id, "discovery-worker", TimeSpan.FromMinutes(5), TimeProvider.System.GetUtcNow(), cancellationToken);
            await scope.ServiceProvider.GetServices<IJobExecutor>().Single(x => x.JobType == JobType.Discovery).ExecuteAsync(job, "discovery-worker", cancellationToken);
            await queue.MarkSucceededAsync(job.Id, "discovery-worker", TimeProvider.System.GetUtcNow(), cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<IDiscoveryService>().GetRunAsync(accepted.DiscoveryRunId, cancellationToken);
            Assert.NotNull(run);
            Assert.Equal(DiscoveryRunStatus.Succeeded, run.Status);
            Assert.Equal(4, run.UrlsDiscovered);
            Assert.Equal(1, run.UrlsAccepted);
            Assert.Equal(1, run.Duplicates);
            Assert.Equal(1, run.Blocked);
            Assert.Equal(1, run.Invalid);
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            Assert.Equal(1, await db.DiscoveryQueries.CountAsync(cancellationToken));
            Assert.Equal(1, await db.DiscoveryRuns.CountAsync(cancellationToken));
            Assert.Equal(1, await db.Jobs.CountAsync(x => x.Type == JobType.Discovery, cancellationToken));
            Assert.Equal(1, await db.CandidatePages.CountAsync(cancellationToken));
            Assert.True(await db.AuditEvents.CountAsync(x => x.Operation.StartsWith("discovery."), cancellationToken) >= 2);
        }
    }

    [Fact]
    public async Task Database_RejectsAuditMutationAndDisabledUsersCannotAuthenticate()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE audit_events SET result = 'tampered'", cancellationToken));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var user = await db.Users.SingleAsync(cancellationToken);
            user.Disable();
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var authenticator = scope.ServiceProvider.GetRequiredService<ICredentialAuthenticator>();
            var authenticated = await authenticator.AuthenticateAsync("bls_integration_012345678901234567890123456789", cancellationToken);
            Assert.Null(authenticated);
        }
    }

    [Fact]
    public async Task AtomicClaim_AllowsOnlyOneWorkerAndRecoversExpiredLease()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var project = new Project("Queue", "example.com", null, now);
        var job = new PersistentJob(JobType.Analysis, project.Id, null, System.Text.Json.JsonSerializer.Serialize(new AnalysisJobPayload(1, project.Id)), 0, now, 3, "claim-test", "claim-test", now);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Projects.Add(project);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        var first = ClaimAsync(provider, "worker-a", TimeSpan.FromMinutes(1), now, cancellationToken);
        var second = ClaimAsync(provider, "worker-b", TimeSpan.FromMinutes(1), now, cancellationToken);
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x is not null);

        var recovered = await ClaimAsync(provider, "worker-c", TimeSpan.FromMinutes(1), now.AddMinutes(1).AddSeconds(1), cancellationToken);
        Assert.NotNull(recovered);
        Assert.Equal("worker-c", recovered.WorkerId);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task AtomicClaim_EnforcesGlobalProjectCampaignAndDomainLimitsAcrossWorkers()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var globalProjects = new[]
        {
            new Project("Global A", "global-a.example", null, now),
            new Project("Global B", "global-b.example", null, now)
        };
        await AssertClaimLimitAsync(
            provider,
            globalProjects,
            null,
            [
                QueueAnalysis(globalProjects[0].Id, null, "global-a.example", "global-a", now),
                QueueAnalysis(globalProjects[1].Id, null, "global-b.example", "global-b", now)
            ],
            new JobClaimLimits(1, 10, 10, 10),
            now,
            cancellationToken);

        var project = new Project("Project cap", "project-cap.example", null, now.AddSeconds(10));
        await AssertClaimLimitAsync(
            provider,
            [project],
            null,
            [
                QueueAnalysis(project.Id, null, "project-a.example", "project-a", now.AddSeconds(10)),
                QueueAnalysis(project.Id, null, "project-b.example", "project-b", now.AddSeconds(10))
            ],
            new JobClaimLimits(10, 1, 10, 10),
            now.AddSeconds(10),
            cancellationToken);

        var campaignProject = new Project("Campaign cap", "campaign-cap.example", null, now.AddSeconds(20));
        var campaign = new Campaign(campaignProject.Id, "Concurrency campaign", CampaignApprovalMode.Manual, "owned", "integration", 100, now.AddSeconds(20));
        await AssertClaimLimitAsync(
            provider,
            [campaignProject],
            campaign,
            [
                QueueAnalysis(campaignProject.Id, campaign.Id, "campaign-a.example", "campaign-a", now.AddSeconds(20)),
                QueueAnalysis(campaignProject.Id, campaign.Id, "campaign-b.example", "campaign-b", now.AddSeconds(20))
            ],
            new JobClaimLimits(10, 10, 1, 10),
            now.AddSeconds(20),
            cancellationToken);

        var domainProjects = new[]
        {
            new Project("Domain A", "domain-a.example", null, now.AddSeconds(30)),
            new Project("Domain B", "domain-b.example", null, now.AddSeconds(30))
        };
        await AssertClaimLimitAsync(
            provider,
            domainProjects,
            null,
            [
                QueueAnalysis(domainProjects[0].Id, null, "shared-source.example", "domain-a", now.AddSeconds(30)),
                QueueAnalysis(domainProjects[1].Id, null, "shared-source.example", "domain-b", now.AddSeconds(30))
            ],
            new JobClaimLimits(10, 10, 10, 1),
            now.AddSeconds(30),
            cancellationToken);
    }

    [Fact]
    public async Task Claim_SkipsSubmissionsBlockedByEveryActionLimit()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var hourly = ActionLimitScenario("Hourly", "hourly.example", 1, 100, 100, 100, "hourly-old.example", "hourly-new.example", now.AddMinutes(-30), now, 100);
        var daily = ActionLimitScenario("Daily", "daily.example", 100, 1, 100, 100, "daily-old.example", "daily-new.example", now.AddHours(-2), now, 90);
        var domain = ActionLimitScenario("Domain", "domain.example", 100, 100, 1, 100, "same-domain.example", "same-domain.example", now.AddHours(-2), now, 80);
        var campaign = ActionLimitScenario("Campaign", "campaign.example", 100, 100, 100, 1, "campaign-old.example", "campaign-new.example", now.AddHours(-2), now, 70);
        var fallbackProject = new Project("Eligible fallback", "fallback.example", null, now);
        var fallback = new PersistentJob(JobType.Analysis, fallbackProject.Id, null, "{\"version\":1}", -100, now, 3, "fallback", "fallback", now, "fallback-source.example");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var scenarios = new[] { hourly, daily, domain, campaign };
            db.Projects.AddRange(scenarios.Select(x => x.Project).Append(fallbackProject));
            db.PolicyDefinitions.AddRange(scenarios.Select(x => x.Policy));
            db.Campaigns.AddRange(scenarios.Select(x => x.Campaign));
            db.Jobs.AddRange(scenarios.SelectMany(x => new[] { x.Completed, x.Queued }).Append(fallback));
            await db.SaveChangesAsync(cancellationToken);
        }

        var claimed = Assert.IsType<PersistentJob>(await ClaimWithLimitsAsync(provider, "action-limit-worker", ClaimLimits, now.AddSeconds(1), cancellationToken));
        Assert.Equal(fallback.Id, claimed.Id);

        await using var verificationScope = provider.CreateAsyncScope();
        var queue = verificationScope.ServiceProvider.GetRequiredService<IJobQueue>();
        foreach (var blocked in new[] { hourly.Queued, daily.Queued, domain.Queued, campaign.Queued })
        {
            Assert.Equal(JobStatus.Queued, (await queue.GetAsync(blocked.Id, cancellationToken))?.Status);
        }
    }

    [Fact]
    public async Task WorkerPlatform_EnforcesDistributedLimitsPauseRecoveryHeartbeatAndRateReservations()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        await MigrateAndSeedAsync(provider, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var project = new Project("Worker Platform", "example.com", null, now);
        var jobs = Enumerable.Range(0, 4).Select(index => new PersistentJob(
            JobType.Analysis,
            project.Id,
            null,
            System.Text.Json.JsonSerializer.Serialize(new AnalysisJobPayload(1, project.Id)),
            0,
            now,
            3,
            $"worker-platform-{index}",
            $"worker-platform-{index}",
            now.AddMilliseconds(index),
            "source.example")).ToArray();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Projects.Add(project);
            db.Jobs.AddRange(jobs);
            await db.SaveChangesAsync(cancellationToken);
        }

        var limitedClaims = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            ClaimWithLimitsAsync(provider, $"limited-{index}", new JobClaimLimits(10, 2, 10, 10), now.AddSeconds(1), cancellationToken)));
        var claimed = limitedClaims.Where(x => x is not null).Cast<PersistentJob>().ToArray();
        Assert.Equal(2, claimed.Length);
        Assert.Equal(2, claimed.Select(x => x.Id).Distinct().Count());

        var pausedJob = claimed[0];
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            await queue.MarkRunningAsync(pausedJob.Id, pausedJob.WorkerId!, TimeSpan.FromMinutes(1), now.AddSeconds(2), cancellationToken);
            var pauseRequested = await queue.PauseAsync(pausedJob.Id, now.AddSeconds(3), cancellationToken);
            Assert.Equal(JobStatus.Running, pauseRequested.Status);
            Assert.NotNull(pauseRequested.PauseRequestedAt);
            Assert.Equal(JobLeaseRenewal.PauseRequested, await queue.RenewLeaseAsync(pausedJob.Id, pausedJob.WorkerId!, TimeSpan.FromMinutes(1), now.AddSeconds(4), cancellationToken));
            await queue.AcknowledgePauseAsync(pausedJob.Id, pausedJob.WorkerId!, now.AddSeconds(5), cancellationToken);
            Assert.Equal(JobStatus.Paused, (await queue.GetAsync(pausedJob.Id, cancellationToken))?.Status);
            Assert.Equal(JobStatus.RetryScheduled, (await queue.ResumeAsync(pausedJob.Id, now.AddSeconds(6), cancellationToken)).Status);
        }

        var exhausted = new PersistentJob(JobType.Analysis, project.Id, null, "{\"version\":1}", 10, now, 1, "exhausted", "exhausted", now.AddSeconds(10));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Jobs.Add(exhausted);
            await db.SaveChangesAsync(cancellationToken);
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            _ = Assert.IsType<PersistentJob>(await queue.ClaimAsync("dead-worker", TimeSpan.FromSeconds(30), ClaimLimits, now.AddSeconds(11), cancellationToken));
        }
        _ = await ClaimWithLimitsAsync(provider, "recovery-worker", ClaimLimits, now.AddSeconds(42), cancellationToken);
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            Assert.Equal(JobStatus.DeadLetter, (await queue.GetAsync(exhausted.Id, cancellationToken))?.Status);

            var stale = new PersistentJob(JobType.Analysis, project.Id, null, "{\"version\":1}", 100, now.AddSeconds(50), 3, "stale", "stale", now.AddSeconds(50));
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Jobs.Add(stale);
            await db.SaveChangesAsync(cancellationToken);
        }

        PersistentJob staleClaim;
        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            staleClaim = Assert.IsType<PersistentJob>(await queue.ClaimAsync("stale-worker", TimeSpan.FromSeconds(30), ClaimLimits, now.AddSeconds(51), cancellationToken));
            await queue.MarkRunningAsync(staleClaim.Id, "stale-worker", TimeSpan.FromSeconds(30), now.AddSeconds(52), cancellationToken);
            Assert.Equal(JobLeaseRenewal.Lost, await queue.RenewLeaseAsync(staleClaim.Id, "stale-worker", TimeSpan.FromSeconds(30), now.AddSeconds(83), cancellationToken));
            await Assert.ThrowsAsync<DomainRuleException>(() => queue.MarkSucceededAsync(staleClaim.Id, "stale-worker", now.AddSeconds(83), cancellationToken));
        }

        var recoveredStale = await ClaimWithLimitsAsync(provider, "replacement-worker", ClaimLimits, now.AddSeconds(84), cancellationToken);
        Assert.NotNull(recoveredStale);
        Assert.Equal(staleClaim.Id, recoveredStale.Id);
        Assert.Equal(1, recoveredStale.RecoveryCount);

        await using (var scope = provider.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            var registry = scope.ServiceProvider.GetRequiredService<IWorkerRegistry>();
            await registry.RegisterAsync(new WorkerRegistration("integration-worker", "integration-host", 42, 4, 8, now), cancellationToken);
            await registry.HeartbeatAsync("integration-worker", 2, now.AddSeconds(1), cancellationToken);
            await registry.MarkStoppedAsync("integration-worker", now.AddSeconds(2), cancellationToken);

            var limiter = scope.ServiceProvider.GetRequiredService<IDomainRateLimiter>();
            await limiter.WaitAsync(project.Id, null, "SOURCE.EXAMPLE", cancellationToken);
            await limiter.WaitAsync(project.Id, null, "source.example", cancellationToken);

            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM worker_heartbeats WHERE worker_id = 'integration-worker' AND stopped_at IS NOT NULL").SingleAsync(cancellationToken));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM domain_rate_limits WHERE domain = 'source.example'").SingleAsync(cancellationToken));
        }
    }

    [Fact]
    public async Task UpgradeFromMilestoneNine_PreservesDataAndInstallsOperationalIndexes()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1", "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        await using var provider = BuildProvider(postgres.GetConnectionString());
        var now = TimeProvider.System.GetUtcNow();
        var project = new Project("Upgrade survivor", "upgrade.example", null, now);
        var job = new PersistentJob(JobType.Analysis, project.Id, null, "{\"version\":1}", 0, now, 3, "upgrade", "upgrade", now);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260820161547_AddReporting", cancellationToken);
            db.Projects.Add(project);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            var beforeUpgrade = await scope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(cancellationToken);
            Assert.True(beforeUpgrade.DatabaseReachable);
            Assert.False(beforeUpgrade.SchemaCurrent);
            Assert.Equal("degraded", beforeUpgrade.Status);

            await migrator.MigrateAsync(cancellationToken: cancellationToken);
            db.ChangeTracker.Clear();

            var afterUpgrade = await scope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(cancellationToken);
            Assert.True(afterUpgrade.DatabaseReachable);
            Assert.True(afterUpgrade.SchemaCurrent);
            Assert.Equal("healthy", afterUpgrade.Status);
            Assert.Equal("Upgrade survivor", (await db.Projects.SingleAsync(x => x.Id == project.Id, cancellationToken)).Name);

            var indexCount = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname IN (
                    'ix_jobs_active_project',
                    'ix_jobs_claim_ready',
                    'ix_jobs_expired_claims',
                    'ix_jobs_submission_campaign_completed',
                    'ix_jobs_submission_project_campaign_completed',
                    'ix_jobs_submission_project_domain_completed')
                """).SingleAsync(cancellationToken);
            Assert.Equal(6, indexCount);

            var snapshot = await scope.ServiceProvider.GetRequiredService<IOperationalMetricsReader>().ReadAsync(now.AddSeconds(1), now.AddMinutes(-1), cancellationToken);
            Assert.Equal(1, snapshot.QueueDepth);
            Assert.Equal(0, snapshot.RunningJobs);
        }
    }

    [Fact]
    public async Task DatabaseOutage_ReportsUnhealthyAndExpiredJobRemainsRecoverable()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_CONTAINER_TESTS") != "1" || Environment.GetEnvironmentVariable("BACKLINKSTUDIO_RUN_FAILURE_TESTS") != "1",
            "Set BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 and BACKLINKSTUDIO_RUN_FAILURE_TESTS=1 on a Docker-enabled host.");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false }.ConnectionString;
        var now = TimeProvider.System.GetUtcNow();
        Guid jobId;

        await using (var provider = BuildProvider(connectionString))
        {
            await MigrateAndSeedAsync(provider, cancellationToken);
            var project = new Project("Failure recovery", "failure.example", null, now);
            var job = new PersistentJob(JobType.Analysis, project.Id, null, "{\"version\":1}", 0, now, 3, "failure", "failure", now);
            jobId = job.Id;
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Projects.Add(project);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var claimed = Assert.IsType<PersistentJob>(await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync("failed-worker", TimeSpan.FromSeconds(30), ClaimLimits, now, cancellationToken));
            await scope.ServiceProvider.GetRequiredService<IJobQueue>().MarkRunningAsync(claimed.Id, "failed-worker", TimeSpan.FromSeconds(30), now, cancellationToken);
        }

        await postgres.StopAsync(cancellationToken);
        await using (var unavailableProvider = BuildProvider(connectionString))
        await using (var unavailableScope = unavailableProvider.CreateAsyncScope())
        {
            var health = await unavailableScope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(cancellationToken);
            Assert.False(health.DatabaseReachable);
            Assert.False(health.SchemaCurrent);
            Assert.Equal("unhealthy", health.Status);
        }

        await postgres.StartAsync(cancellationToken);
        var recoveredConnectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false }.ConnectionString;
        await using (var probe = new NpgsqlConnection(recoveredConnectionString))
        {
            await probe.OpenAsync(cancellationToken);
        }
        await using (var recoveredProvider = BuildProvider(recoveredConnectionString))
        {
            SystemHealthDto? health = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await using var healthScope = recoveredProvider.CreateAsyncScope();
                health = await healthScope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(cancellationToken);
                if (health.DatabaseReachable && health.SchemaCurrent) break;
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            Assert.NotNull(health);
            Assert.True(health.DatabaseReachable);
            Assert.True(health.SchemaCurrent);
            await using var recoveredScope = recoveredProvider.CreateAsyncScope();
            var recovered = Assert.IsType<PersistentJob>(await recoveredScope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync(
                "replacement-worker",
                TimeSpan.FromSeconds(30),
                ClaimLimits,
                now.AddSeconds(31),
                cancellationToken));
            Assert.Equal(jobId, recovered.Id);
            Assert.Equal(1, recovered.RecoveryCount);
        }
    }

    private static async Task<PersistentJob?> ClaimAsync(ServiceProvider provider, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync(workerId, lease, ClaimLimits, now, cancellationToken);
    }

    private static async Task<PersistentJob?> ClaimWithLimitsAsync(ServiceProvider provider, string workerId, JobClaimLimits limits, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobQueue>().ClaimAsync(workerId, TimeSpan.FromMinutes(1), limits, now, cancellationToken);
    }

    private static async Task AssertClaimLimitAsync(
        ServiceProvider provider,
        IReadOnlyCollection<Project> projects,
        Campaign? campaign,
        IReadOnlyList<PersistentJob> jobs,
        JobClaimLimits limits,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>();
            db.Projects.AddRange(projects);
            if (campaign is not null)
            {
                db.Campaigns.Add(campaign);
            }
            db.Jobs.AddRange(jobs);
            await db.SaveChangesAsync(cancellationToken);
        }

        var claims = await Task.WhenAll(jobs.Select((_, index) =>
            ClaimWithLimitsAsync(provider, $"limit-worker-{now.ToUnixTimeMilliseconds()}-{index}", limits, now.AddSeconds(1), cancellationToken)));
        var claimed = Assert.Single(claims, x => x is not null)!;
        var unclaimed = Assert.Single(jobs, x => x.Id != claimed.Id);

        await using var cleanupScope = provider.CreateAsyncScope();
        var queue = cleanupScope.ServiceProvider.GetRequiredService<IJobQueue>();
        await queue.MarkRunningAsync(claimed.Id, claimed.WorkerId!, TimeSpan.FromMinutes(1), now.AddSeconds(2), cancellationToken);
        await queue.MarkSucceededAsync(claimed.Id, claimed.WorkerId!, now.AddSeconds(3), cancellationToken);
        await queue.PauseAsync(unclaimed.Id, now.AddSeconds(3), cancellationToken);
    }

    private static PersistentJob QueueAnalysis(Guid projectId, Guid? campaignId, string domain, string key, DateTimeOffset now) =>
        new(JobType.Analysis, projectId, campaignId, "{\"version\":1}", 0, now, 3, key, key, now, domain);

    private static ActionLimitData ActionLimitScenario(
        string name,
        string projectDomain,
        int hourlyLimit,
        int dailyLimit,
        int perDomainLimit,
        int campaignDailyLimit,
        string completedDomain,
        string queuedDomain,
        DateTimeOffset completedAt,
        DateTimeOffset now,
        int priority)
    {
        var project = new Project($"{name} action limit", projectDomain, null, now.AddDays(-1));
        var policy = new PolicyDefinition(project.Id, now.AddDays(-1));
        policy.Update(true, 0, 100, false, hourlyLimit, dailyLimit, perDomainLimit, now.AddDays(-1));
        var campaign = new Campaign(project.Id, $"{name} campaign", CampaignApprovalMode.Manual, "owned", $"{name}-integration", campaignDailyLimit, now.AddDays(-1));
        campaign.Start(now.AddDays(-1));

        var completed = new PersistentJob(JobType.Submission, project.Id, campaign.Id, "{\"version\":1}", 0, completedAt.AddMinutes(-2), 3, $"{name}-completed", $"{name}-completed", completedAt.AddMinutes(-2), completedDomain);
        completed.Claim("history-worker", completedAt.AddMinutes(-1), TimeSpan.FromMinutes(5));
        completed.Start("history-worker", completedAt, TimeSpan.FromMinutes(5));
        completed.Succeed("history-worker", completedAt.AddSeconds(1));
        var queued = new PersistentJob(JobType.Submission, project.Id, campaign.Id, "{\"version\":1}", priority, now, 3, $"{name}-queued", $"{name}-queued", now, queuedDomain);
        return new ActionLimitData(project, policy, campaign, completed, queued);
    }

    private sealed record ActionLimitData(Project Project, PolicyDefinition Policy, Campaign Campaign, PersistentJob Completed, PersistentJob Queued);

    private static ServiceProvider BuildProvider(string connectionString, int? submissionPort = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BacklinkStudio"] = connectionString,
            ["BacklinkStudio:BootstrapApiKey"] = "bls_integration_012345678901234567890123456789",
            ["BacklinkStudio:BootstrapName"] = "Integration Administrator",
            ["JobPlatform:PerDomainRequestIntervalMilliseconds"] = "10",
            ["JobPlatform:MaximumRateLimitWaitSeconds"] = "10",
            ["Submission:AllowInsecureLoopbackHttpForTesting"] = submissionPort.HasValue ? "true" : "false",
            ["Submission:Profiles:local-owned:Endpoint"] = submissionPort.HasValue ? $"http://127.0.0.1:{submissionPort}/submit" : null,
            ["Submission:Profiles:local-owned:SourceDomain"] = submissionPort.HasValue ? "127.0.0.1" : null,
            ["Submission:Profiles:local-owned:AllowedTypes:0"] = submissionPort.HasValue ? "OwnedProperty" : null,
            ["Reporting:StoragePath"] = Path.Combine(Path.GetTempPath(), "backlinkstudio-report-tests", Guid.NewGuid().ToString("N"))
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBacklinkStudioInfrastructure(configuration);
        services.AddBacklinkStudioDiscovery();
        services.AddBacklinkStudioOpportunities();
        services.AddBacklinkStudioSubmission(configuration, "Test");
        services.AddBacklinkStudioVerification();
        services.AddBacklinkStudioScheduling();
        services.AddBacklinkStudioReporting();
        services.AddScoped<ISiteAnalyzer>(_ => new DeterministicSiteAnalyzer());
        services.AddScoped<IBacklinkVerifier>(_ => new DeterministicBacklinkVerifier());
        services.AddScoped<IJobExecutor, AnalysisJobExecutor>();
        services.AddSingleton<IConfiguration>(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<string?> AcceptSubmissionAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? idempotencyKey = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line && line.Length > 0)
        {
            if (line.StartsWith("Idempotency-Key:", StringComparison.OrdinalIgnoreCase)) idempotencyKey = line[(line.IndexOf(':') + 1)..].Trim();
        }
        var response = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 201 Created\r\nContent-Length: 0\r\nX-Submission-Reference: local-1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
        return idempotencyKey;
    }

    private sealed class DeterministicSiteAnalyzer : ISiteAnalyzer
    {
        public Task<SiteAnalysisResult> AnalyzeAsync(Uri url, CancellationToken cancellationToken) => Task.FromResult(new SiteAnalysisResult(
            url.AbsoluteUri, url.AbsoluteUri, 200, "text/html", "Useful Resources", null, url.IdnHost, "Generic", "index,follow",
            [], ["resource-page"], false, 5, 2, 400, DateTimeOffset.UtcNow, null, TimeSpan.FromMilliseconds(1)));
    }

    private sealed class DeterministicBacklinkVerifier : IBacklinkVerifier
    {
        public Task<BacklinkVerificationResult> VerifyAsync(Uri sourceUrl, string normalizedTargetUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new BacklinkVerificationResult(sourceUrl.AbsoluteUri, sourceUrl.AbsoluteUri, true, 200, "Owned anchor", ["nofollow"], sourceUrl.AbsoluteUri, DateTimeOffset.UtcNow, null, TimeSpan.FromMilliseconds(2)));
    }

    private static readonly JobClaimLimits ClaimLimits = new(100, 100, 100, 100);

    private static async Task MigrateAndSeedAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        await DatabaseOperations.MigrateAndSeedAsync(
            scope.ServiceProvider.GetRequiredService<BacklinkStudioDbContext>(),
            scope.ServiceProvider.GetRequiredService<IApiKeyHasher>(),
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            cancellationToken);
    }
}
