using BacklinkStudio.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Opportunities;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioOpportunities(this IServiceCollection services)
    {
        services.AddScoped<ICmsDetector, WordPressDetector>();
        services.AddScoped<ICmsDetector, GenericDetector>();
        services.AddScoped<IOpportunityClassifier, OpportunityClassifier>();
        services.AddScoped<IOpportunityScorer, OpportunityScorer>();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IPolicyService, PolicyService>();
        return services;
    }
}
