using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure;
using BacklinkStudio.Mcp;
using BacklinkStudio.Mcp.Protocol;
using BacklinkStudio.Mcp.Security;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Reporting;
using BacklinkStudio.Scheduling;
using BacklinkStudio.Submission;
using BacklinkStudio.Verification;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var stdio = args.Contains("--stdio", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args);
var mcpRateLimit = builder.Configuration.GetSection(McpRateLimitOptions.SectionName).Get<McpRateLimitOptions>() ?? new McpRateLimitOptions();
mcpRateLimit.Validate();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
if (stdio)
{
    builder.Logging.ClearProviders();
}
else
{
    builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console(new RenderedCompactJsonFormatter()));
}

builder.Services.AddBacklinkStudioInfrastructure(builder.Configuration);
builder.Services.AddBacklinkStudioDiscovery();
builder.Services.AddBacklinkStudioOpportunities();
builder.Services.AddBacklinkStudioSubmission(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddBacklinkStudioVerification();
builder.Services.AddBacklinkStudioScheduling();
builder.Services.AddBacklinkStudioReporting();
builder.Services.AddScoped<McpToolDispatcher>();
builder.Services.AddMcpSecurity();
builder.Services.AddHealthChecks().AddCheck<McpDatabaseHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = mcpRateLimit.PermitLimit, Window = TimeSpan.FromSeconds(mcpRateLimit.WindowSeconds), QueueLimit = 0 }));
});

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("backlinkstudio-mcp"))
    .WithTracing(tracing => tracing.AddSource(BacklinkStudioTelemetry.SourceName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddMeter(BacklinkStudioTelemetry.SourceName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

var app = builder.Build();
if (stdio)
{
    var stdioScopes = (builder.Configuration.GetSection(StdioMcpOptions.SectionName).Get<StdioMcpOptions>() ?? new StdioMcpOptions()).Resolve();
    await StdioMcpServer.RunAsync(app.Services, stdioScopes, CancellationToken.None);
    return;
}

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var health = await scope.ServiceProvider.GetRequiredService<ISystemHealthService>().CheckAsync(CancellationToken.None);
    Environment.ExitCode = health.DatabaseReachable && health.SchemaCurrent ? 0 : 1;
    return;
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BacklinkStudio.Mcp.Unhandled");
    McpLog.UnhandledRequest(logger, context.TraceIdentifier, exception);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(
        McpResponse.Failure(null, -32603, "Internal error"),
        context.RequestAborted);
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/health/ready", async (ISystemHealthService healthService, CancellationToken cancellationToken) =>
{
    var health = await healthService.CheckAsync(cancellationToken);
    return Results.Json(health, statusCode: health.DatabaseReachable && health.SchemaCurrent
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable);
});
app.MapPost("/mcp", async (HttpContext context, McpToolDispatcher dispatcher, CancellationToken cancellationToken) =>
{
    McpRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<McpRequest>(context.Request.Body, McpJson.Options, cancellationToken);
    }
    catch (JsonException)
    {
        return Results.Json(McpResponse.Failure(null, -32700, "Parse error"));
    }

    if (request is null)
    {
        return Results.Json(McpResponse.Failure(null, -32600, "Invalid Request"));
    }

    var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    Guid? credentialId = Guid.TryParse(context.User.FindFirstValue("credential_id"), out var parsed) ? parsed : null;
    var actor = new ActorContext(ActorType.Agent, actorId, credentialId, context.TraceIdentifier, context.Connection.RemoteIpAddress?.ToString());
    var scopes = context.User.FindAll("scope").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
    var response = await dispatcher.DispatchAsync(request, actor, scopes, cancellationToken);
    return response is null ? Results.NoContent() : Results.Json(response);
}).RequireAuthorization("mcp");
await app.RunAsync();

public partial class Program;

internal static partial class McpLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled MCP request failure. TraceId {TraceId}")]
    public static partial void UnhandledRequest(Microsoft.Extensions.Logging.ILogger logger, string traceId, Exception? exception);
}

internal static class McpJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
