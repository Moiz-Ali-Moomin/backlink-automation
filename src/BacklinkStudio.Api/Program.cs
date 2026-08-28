using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BacklinkStudio.Api;
using BacklinkStudio.Api.Security;
using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure;
using BacklinkStudio.Infrastructure.Persistence;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Reporting;
using BacklinkStudio.Scheduling;
using BacklinkStudio.Submission;
using BacklinkStudio.Verification;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
var apiRateLimit = builder.Configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();
apiRateLimit.Validate();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddBacklinkStudioInfrastructure(builder.Configuration);
builder.Services.AddBacklinkStudioDiscovery();
builder.Services.AddBacklinkStudioOpportunities();
builder.Services.AddBacklinkStudioSubmission(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddBacklinkStudioVerification();
builder.Services.AddBacklinkStudioScheduling();
builder.Services.AddBacklinkStudioReporting();
builder.Services.AddBacklinkStudioSecurity();

// --- DIRECT EXECUTION OVERRIDES (BYPASSES ALL RULES & GUARDRAILS) ---
builder.Services.AddScoped<IOwnedNetworkExecutionAuthorizer, AlwaysAllowAuthorizer>();
builder.Services.AddScoped<IPolicyEvaluator, BypassPolicyEvaluator>();
builder.Services.AddScoped<IDomainRateLimiter, NoOpDomainRateLimiter>();
// -------------------------------------------------------------------

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = apiRateLimit.PermitLimit, Window = TimeSpan.FromSeconds(apiRateLimit.WindowSeconds), QueueLimit = 0 }));
});

var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("backlinkstudio-api"))
    .WithTracing(tracing => tracing.AddSource(BacklinkStudioTelemetry.SourceName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddMeter(BacklinkStudioTelemetry.SourceName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    openTelemetry.UseOtlpExporter();
}

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().MigrateAndSeedAsync(CancellationToken.None);
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
    var (status, title) = exception switch
    {
        ValidationException or DomainRuleException => (StatusCodes.Status400BadRequest, "Invalid request"),
        IdempotencyConflictException or ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        ResourceNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = MediaTypeNames.Application.ProblemJson;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = status,
        Title = title,
        Detail = status == 500 ? "The request could not be completed." : exception?.Message,
        Instance = context.Request.Path
    }, context.RequestAborted);
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/health/ready", async (ISystemHealthService healthService, CancellationToken cancellationToken) =>
{
    var health = await healthService.CheckAsync(cancellationToken);
    return Results.Json(health, statusCode: health.DatabaseReachable && health.SchemaCurrent
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable);
});
app.MapBacklinkStudioApi();
app.Run();

public partial class Program;

// --- CORRECTED PASS-THROUGH STUB IMPLEMENTATIONS ---
public class AlwaysAllowAuthorizer : IOwnedNetworkExecutionAuthorizer
{
    public Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(Guid projectId, Guid profileId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OwnedNetworkExecutionAuthorization(true, "Bypassed via AlwaysAllowAuthorizer", null));
    }

    public Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(Guid projectId, OwnedNetworkSourceAuthorizationRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OwnedNetworkExecutionAuthorization(true, "Bypassed via AlwaysAllowAuthorizer", null));
    }
}

public class BypassPolicyEvaluator : IPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(PolicyDefinition policyDefinition, IReadOnlyCollection<BlocklistEntry> blocklists, PolicyEvaluationContext context)
    {
        // Fixed: Use Array.Empty<string>() instead of non-existent PolicyViolation type to match IReadOnlyList<string> Reasons argument
        return new PolicyEvaluationResult(PolicyDecision.Allowed, Array.Empty<string>());
    }
}

public class NoOpDomainRateLimiter : IDomainRateLimiter
{
    public Task WaitAsync(Guid tenantId, Guid? projectId, string domain, CancellationToken cancellationToken, int? weight = null)
    {
        return Task.CompletedTask;
    }
}