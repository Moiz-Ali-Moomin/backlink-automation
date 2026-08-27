using BacklinkStudio.Application;
using BacklinkStudio.Infrastructure.Http;
using BacklinkStudio.Infrastructure.Persistence;
using BacklinkStudio.Infrastructure.Reporting;
using BacklinkStudio.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BacklinkStudio");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:BacklinkStudio is required.");
        }

        services.AddDbContext<BacklinkStudioDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(BacklinkStudioDbContext).Assembly.FullName)));
        services.AddScoped<StudioRepository>();
        services.AddScoped<IProjectRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IOwnedNetworkRepository, OwnedNetworkRepository>();
        services.AddScoped<ISubmissionSourceRepository, SubmissionSourceRepository>();
        services.AddScoped<IBacklinkWorkflowRepository, BacklinkWorkflowRepository>();
        services.AddScoped<SubmissionContentRepository>();
        services.AddScoped<ISubmissionContentRepository>(x => x.GetRequiredService<SubmissionContentRepository>());
        services.AddScoped<IWordPressSiteProfileRepository>(x => x.GetRequiredService<SubmissionContentRepository>());
        services.AddScoped<IOwnedNetworkCampaignRepository, OwnedNetworkCampaignRepository>();
        services.AddScoped<ICandidateRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IOpportunityRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IPolicyRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IDiscoveryRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<ISubmissionRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IBacklinkRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IReportRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IReportDataSource>(x => x.GetRequiredService<StudioRepository>());
        services.AddSingleton<IReportArtifactStore, LocalReportArtifactStore>();
        services.AddScoped<IAgentCredentialRepository>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IScheduleStore, PostgresScheduleStore>();
        services.AddScoped<IAuditSink>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IIdempotencyStore>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IStudioUnitOfWork>(x => x.GetRequiredService<StudioRepository>());
        services.AddScoped<IJobQueue, PostgresJobQueue>();
        services.AddScoped<IWorkerRegistry, PostgresWorkerRegistry>();
        services.AddScoped<IDomainRateLimiter, PostgresDomainRateLimiter>();
        services.AddSingleton<IJobFailureClassifier, DefaultJobFailureClassifier>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();
        services.AddSingleton<IAgentApiKeyFactory, AgentApiKeyFactory>();
        services.AddScoped<IAgentCredentialService, AgentCredentialService>();
        services.AddScoped<ICredentialAuthenticator, CredentialAuthenticator>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IOperationalMetricsReader, PostgresOperationalMetricsReader>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUrlNormalizer, UrlNormalizer>();
        services.AddOptions<JobPlatformOptions>()
            .Bind(configuration.GetSection(JobPlatformOptions.SectionName))
            .Validate(x => x.PerDomainRequestIntervalMilliseconds is >= 1 and <= 60_000, "Per-domain request interval must be between 1 ms and 60 seconds.")
            .Validate(x => x.MaximumRateLimitWaitSeconds is >= 1 and <= 3_600, "Maximum rate-limit wait must be between 1 second and 1 hour.")
            .ValidateOnStart();
        services.AddOptions<ReportStorageOptions>()
            .Bind(configuration.GetSection(ReportStorageOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.StoragePath) && x.StoragePath.Length <= 1_024, "Reporting storage path is invalid.")
            .ValidateOnStart();
        services.AddOptions<AnalysisHttpOptions>()
            .Bind(configuration.GetSection(AnalysisHttpOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 120, "Analysis HTTP timeout must be between 1 and 120 seconds.")
            .Validate(x => x.MaximumResponseBytes is >= 16_384 and <= 10_485_760, "Analysis response limit must be between 16 KiB and 10 MiB.")
            .Validate(x => x.MaximumRedirects is >= 0 and <= 10, "Analysis redirect limit must be between 0 and 10.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.UserAgent) && x.UserAgent.Length <= 200, "Analysis user agent is invalid.")
            .ValidateOnStart();
        services.AddHttpClient<ISiteAnalyzer, AnalysisHttpClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnalysisHttpOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(SafeNetworkHandler.Create).RemoveAllLoggers();
        services.AddOptions<SubmissionSourceHttpOptions>()
            .Bind(configuration.GetSection(SubmissionSourceHttpOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 120, "Submission source HTTP timeout must be between 1 and 120 seconds.")
            .Validate(x => x.MaximumResponseBytes is >= 16_384 and <= 10_485_760, "Submission source response limit must be between 16 KiB and 10 MiB.")
            .Validate(x => x.MaximumRedirects is >= 0 and <= 10, "Submission source redirect limit must be between 0 and 10.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.UserAgent) && x.UserAgent.Length <= 200, "Submission source user agent is invalid.")
            .Validate(x => x.AllowedPrivateHosts.Length <= 1_000 && x.AllowedPrivateHosts.All(host =>
                host.Length is >= 1 and <= 253 && Uri.CheckHostName(host.Trim().TrimEnd('.')) != UriHostNameType.Unknown),
                "Submission source private hosts must be a bounded list of DNS names or IP addresses.")
            .ValidateOnStart();
        services.AddHttpClient<ISubmissionSourceInspector, SubmissionSourceHttpInspector>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SubmissionSourceHttpOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(provider => OwnedNetworkSafeNetworkHandler.Create(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SubmissionSourceHttpOptions>>().Value)).RemoveAllLoggers();
        services.AddOptions<WordPressSubmissionOptions>()
            .Bind(configuration.GetSection(WordPressSubmissionOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 120, "WordPress submission timeout must be between 1 and 120 seconds.")
            .Validate(x => x.MaximumResponseBytes is >= 16_384 and <= 10_485_760, "WordPress response limit must be between 16 KiB and 10 MiB.")
            .Validate(x => x.AllowedInsecureControlledHttpHosts.Length <= 1_000 &&
                x.AllowedInsecureControlledHttpHosts.All(host => host.Length is >= 1 and <= 253 &&
                    Uri.CheckHostName(host.Trim().TrimEnd('.')) != UriHostNameType.Unknown),
                "WordPress insecure controlled HTTP hosts must be a bounded list of exact DNS names or IP addresses.")
            .Validate(x => x.Credentials.Count <= 1_000 && x.Credentials.All(item => item.Key.Length is >= 1 and <= 200 &&
                item.Value.Username.Length is >= 1 and <= 320 && item.Value.ApplicationPassword.Length is >= 1 and <= 8_192),
                "WordPress credentials must use bounded named server-side references.")
            .ValidateOnStart();
        services.AddOptions<WordPressFallbackOptions>()
            .Bind(configuration.GetSection(WordPressFallbackOptions.SectionName))
            .Validate(x => x.MaximumStrategies is >= 1 and <= 4,
                "WordPress fallback strategies must be between 1 and 4.")
            .ValidateOnStart();
        services.AddOptions<ControlledBrowserOptions>()
            .Bind(configuration.GetSection(ControlledBrowserOptions.SectionName))
            .Validate(x => x.MaximumConcurrency is >= 1 and <= 4,
                "Controlled browser concurrency must be between 1 and 4.")
            .Validate(x => x.NavigationTimeoutMilliseconds is >= 1_000 and <= 120_000,
                "Controlled browser navigation timeout must be between 1 and 120 seconds.")
            .Validate(x => x.ActionTimeoutMilliseconds is >= 500 and <= 60_000,
                "Controlled browser action timeout must be between 0.5 and 60 seconds.")
            .Validate(x => x.MaximumDomBytes is >= 16_384 and <= 10_485_760,
                "Controlled browser DOM limit must be between 16 KiB and 10 MiB.")
            .ValidateOnStart();
        services.AddHttpClient<IWordPressSubmissionGateway, WordPressSubmissionGateway>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WordPressSubmissionOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BacklinkStudio-WordPressAdapter/1.0");
        }).ConfigurePrimaryHttpMessageHandler(provider => OwnedNetworkSafeNetworkHandler.Create(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SubmissionSourceHttpOptions>>().Value)).RemoveAllLoggers();
        services.AddHttpClient<IOwnedWordPressFallbackCommentAdapter, OwnedWordPressFallbackCommentAdapter>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WordPressSubmissionOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BacklinkStudio-WordPressFallbackAdapter/1.0");
        }).ConfigurePrimaryHttpMessageHandler(provider => OwnedNetworkSafeNetworkHandler.Create(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SubmissionSourceHttpOptions>>().Value)).RemoveAllLoggers();
        services.AddSingleton<ControlledBrowserRuntime>();
        services.AddScoped<IControlledBrowserValidationAdapter, ControlledBrowserValidationAdapter>();
        services.AddScoped<IControlledBrowserCommentAdapter, ControlledBrowserCommentAdapter>();
        services.AddScoped<IControlledBrowserBacklinkVerifier, ControlledBrowserBacklinkVerifier>();
        services.AddOptions<DiscoveryHttpOptions>()
            .Bind(configuration.GetSection(DiscoveryHttpOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 120, "Discovery HTTP timeout must be between 1 and 120 seconds.")
            .Validate(x => x.MaximumResponseBytes is >= 16_384 and <= 10_485_760, "Discovery response limit must be between 16 KiB and 10 MiB.")
            .Validate(x => x.MaximumRedirects is >= 0 and <= 10, "Discovery redirect limit must be between 0 and 10.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.UserAgent) && x.UserAgent.Length <= 200, "Discovery user agent is invalid.")
            .ValidateOnStart();
        services.AddOptions<SerperOptions>().Bind(configuration.GetSection(SerperOptions.SectionName));
        services.AddHttpClient<IDiscoveryDocumentClient, DiscoveryDocumentHttpClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscoveryHttpOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(SafeNetworkHandler.Create).RemoveAllLoggers();
        services.AddHttpClient<ISerperClient, SerperHttpClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscoveryHttpOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(SafeNetworkHandler.Create).RemoveAllLoggers();
        services.AddOptions<VerificationHttpOptions>()
            .Bind(configuration.GetSection(VerificationHttpOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 120, "Verification HTTP timeout must be between 1 and 120 seconds.")
            .Validate(x => x.MaximumResponseBytes is >= 16_384 and <= 10_485_760, "Verification response limit must be between 16 KiB and 10 MiB.")
            .Validate(x => x.MaximumRedirects is >= 0 and <= 10, "Verification redirect limit must be between 0 and 10.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.UserAgent) && x.UserAgent.Length <= 200, "Verification user agent is invalid.")
            .Validate(x => x.AllowedPrivateHosts.Length <= 1_000 && x.AllowedPrivateHosts.All(host =>
                host.Length is >= 1 and <= 253 && Uri.CheckHostName(host.Trim().TrimEnd('.')) != UriHostNameType.Unknown),
                "Verification private hosts must be a bounded list of DNS names or IP addresses.")
            .ValidateOnStart();
        services.AddHttpClient<IBacklinkVerifier, VerificationHttpClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<VerificationHttpOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(provider => OwnedNetworkSafeNetworkHandler.Create(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<VerificationHttpOptions>>().Value)).RemoveAllLoggers();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IJobService, JobService>();
        return services;
    }
}
