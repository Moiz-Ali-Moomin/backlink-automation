using BacklinkStudio.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Reporting;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioReporting(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IJobExecutor, ReportJobExecutor>();
        return services;
    }
}
