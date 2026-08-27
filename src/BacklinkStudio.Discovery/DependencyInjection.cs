using BacklinkStudio.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Discovery;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioDiscovery(this IServiceCollection services)
    {
        services.AddScoped<IDiscoveryProvider, ManualUrlProvider>();
        services.AddScoped<IDiscoveryProvider, TxtImportProvider>();
        services.AddScoped<IDiscoveryProvider, CsvImportProvider>();
        services.AddScoped<IDiscoveryProvider, SitemapProvider>();
        services.AddScoped<IDiscoveryProvider, SerperProvider>();
        services.AddScoped<IDiscoveryProvider, CompetitorBacklinkImportProvider>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<ICandidateService, CandidateService>();
        services.AddScoped<IJobExecutor, DiscoveryJobExecutor>();
        return services;
    }
}
