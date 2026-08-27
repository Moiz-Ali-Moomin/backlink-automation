using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Infrastructure;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Reporting;
using BacklinkStudio.Scheduling;
using BacklinkStudio.Submission;
using BacklinkStudio.Verification;
using BacklinkStudio.Worker;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console(new RenderedCompactJsonFormatter()));
builder.Services.AddBacklinkStudioInfrastructure(builder.Configuration);
builder.Services.AddBacklinkStudioDiscovery();
builder.Services.AddBacklinkStudioOpportunities();
builder.Services.AddBacklinkStudioSubmission(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddBacklinkStudioVerification();
builder.Services.AddBacklinkStudioScheduling();
builder.Services.AddBacklinkStudioReporting();
builder.Services.AddScoped<IJobExecutor, AnalysisJobExecutor>();
builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .Validate(x => x.Concurrency is >= 1 and <= 32, "Worker concurrency must be between 1 and 32.")
    .Validate(x => x.BufferSize is >= 1 and <= 1_000, "Worker buffer size must be between 1 and 1000.")
    .Validate(x => x.PollIntervalMilliseconds is >= 100 and <= 60_000, "Worker poll interval is invalid.")
    .Validate(x => x.ClaimLeaseSeconds is >= 30 and <= 3_600, "Worker claim lease is invalid.")
    .Validate(x => x.LeaseRenewalSeconds is >= 5 and <= 1_200 && x.LeaseRenewalSeconds * 2 < x.ClaimLeaseSeconds, "Lease renewal must be at least 5 seconds and less than half the claim lease.")
    .Validate(x => x.HeartbeatIntervalSeconds is >= 5 and <= 300, "Worker heartbeat interval is invalid.")
    .Validate(x => x.GlobalConcurrency is >= 1 and <= 10_000, "Global concurrency is invalid.")
    .Validate(x => x.PerProjectConcurrency is >= 1 and <= 1_000, "Per-project concurrency is invalid.")
    .Validate(x => x.PerCampaignConcurrency is >= 1 and <= 1_000, "Per-campaign concurrency is invalid.")
    .Validate(x => x.PerDomainConcurrency is >= 1 and <= 100, "Per-domain concurrency is invalid.")
    .Validate(x => x.Concurrency <= x.GlobalConcurrency && x.PerDomainConcurrency <= x.PerProjectConcurrency && x.PerCampaignConcurrency <= x.PerProjectConcurrency, "Worker concurrency limits are inconsistent.")
    .Validate(x => x.RetryBaseDelaySeconds is >= 1 and <= 300, "Retry base delay is invalid.")
    .Validate(x => x.RetryMaximumDelaySeconds is >= 1 and <= 86_400 && x.RetryMaximumDelaySeconds >= x.RetryBaseDelaySeconds, "Retry maximum delay is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<SchedulerOptions>()
    .Bind(builder.Configuration.GetSection(SchedulerOptions.SectionName))
    .Validate(x => x.PollIntervalMilliseconds is >= 100 and <= 60_000, "Scheduler poll interval is invalid.")
    .Validate(x => x.ClaimLeaseSeconds is >= 30 and <= 3_600, "Scheduler claim lease is invalid.")
    .Validate(x => x.LeaseRenewalSeconds is >= 5 and <= 1_200 && x.LeaseRenewalSeconds * 2 < x.ClaimLeaseSeconds, "Scheduler lease renewal must be less than half the claim lease.")
    .Validate(x => x.FailureRetrySeconds is >= 1 and <= 3_600, "Scheduler failure retry is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<OperationalMetricsOptions>()
    .Bind(builder.Configuration.GetSection(OperationalMetricsOptions.SectionName))
    .Validate(x => x.PollIntervalSeconds is >= 5 and <= 300, "Operational metrics poll interval is invalid.")
    .Validate(x => x.WorkerStaleSeconds is >= 15 and <= 3_600, "Operational metrics worker staleness window is invalid.")
    .ValidateOnStart();

var configuredOptions = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
builder.Services.AddSingleton<IRetryDelayPolicy>(new ExponentialBackoffRetryPolicy(
    TimeSpan.FromSeconds(configuredOptions.RetryBaseDelaySeconds),
    TimeSpan.FromSeconds(configuredOptions.RetryMaximumDelaySeconds)));
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(Math.Min(configuredOptions.ClaimLeaseSeconds, 300)));
if (configuredOptions.Enabled)
{
    builder.Services.AddHostedService<PersistentJobWorker>();
}
var configuredScheduler = builder.Configuration.GetSection(SchedulerOptions.SectionName).Get<SchedulerOptions>() ?? new SchedulerOptions();
if (configuredScheduler.Enabled)
{
    builder.Services.AddHostedService<PersistentScheduler>();
}
builder.Services.AddHostedService<OperationalMetricsPublisher>();

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(configuredOptions.Enabled ? "backlinkstudio-worker" : "backlinkstudio-scheduler"))
    .WithTracing(tracing => tracing.AddSource(BacklinkStudioTelemetry.SourceName).AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddMeter(BacklinkStudioTelemetry.SourceName).AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

var app = builder.Build();
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var health = await scope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(CancellationToken.None);
    Environment.ExitCode = health.DatabaseReachable && health.SchemaCurrent ? 0 : 1;
    return;
}

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (ISystemHealthService health, CancellationToken cancellationToken) =>
{
    var result = await health.CheckAsync(cancellationToken);
    return result.DatabaseReachable && result.SchemaCurrent ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});
await app.RunAsync();

public partial class Program;
