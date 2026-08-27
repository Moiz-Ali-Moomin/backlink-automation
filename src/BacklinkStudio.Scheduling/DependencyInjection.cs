using BacklinkStudio.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Scheduling;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioScheduling(this IServiceCollection services)
    {
        services.AddSingleton<IScheduleOccurrenceCalculator, ScheduleOccurrenceCalculator>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IScheduleRunner, ScheduleRunner>();
        return services;
    }
}
