using BacklinkStudio.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Submission;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioSubmission(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        var testOwnershipOverride = TestOwnershipOverrideOptions.FromConfiguration(configuration, environmentName);
        services.AddSingleton(testOwnershipOverride);
        services.AddScoped<OwnedNetworkExecutionAuthorizer>();
        services.AddScoped<IOwnedNetworkExecutionAuthorizer>(provider =>
        {
            var authorizer = provider.GetRequiredService<OwnedNetworkExecutionAuthorizer>();
            return testOwnershipOverride.Enabled
                ? new TestOwnershipExecutionAuthorizerDecorator(authorizer, testOwnershipOverride)
                : authorizer;
        });
        services.AddSingleton<IValidateOptions<SubmissionOptions>, SubmissionOptionsValidator>();
        services.AddOptions<SubmissionOptions>().Bind(configuration.GetSection(SubmissionOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<ISubmissionAuthorizationResolver, SubmissionAuthorizationResolver>();
        services.AddHttpClient<ISubmissionAdapter, OwnedEndpointSubmissionAdapter>((provider, client) => client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<SubmissionOptions>>().Value.TimeoutSeconds)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<IOwnedNetworkService, OwnedNetworkService>();
        services.AddScoped<ISubmissionSourceService, SubmissionSourceService>();
        services.AddScoped<ISubmissionIdentityService, SubmissionIdentityService>();
        services.AddScoped<ISubmissionTemplateService, SubmissionTemplateService>();
        services.AddScoped<ISubmissionContentResolver, SubmissionContentResolver>();
        services.AddScoped<ISubmissionPreviewService, SubmissionPreviewService>();
        services.AddScoped<IOwnedWordPressCommentAdapter, OwnedWordPressCommentAdapter>();
        services.AddScoped<IWordPressSiteProfileService, WordPressSiteProfileService>();
        services.AddScoped<IOwnedNetworkWorkflowService, OwnedNetworkWorkflowService>();
        services.AddScoped<IBacklinkWorkflowService, BacklinkWorkflowService>();
        services.AddScoped<IJobExecutor, SubmissionJobExecutor>();
        services.AddScoped<IJobExecutor, SubmissionSourceImportJobExecutor>();
        services.AddScoped<IJobExecutor, SubmissionSourceValidationJobExecutor>();
        services.AddScoped<IJobExecutor, OwnedNetworkWorkflowJobExecutor>();
        services.AddScoped<IJobExecutor, BacklinkWorkflowJobExecutor>();
        services.AddScoped<IJobExecutor, OwnedNetworkCampaignExpansionJobExecutor>();
        return services;
    }
}
