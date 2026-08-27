using BacklinkStudio.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.Verification;

public static class DependencyInjection
{
    public static IServiceCollection AddBacklinkStudioVerification(this IServiceCollection services)
    {
        services.AddScoped<IVerificationService, VerificationService>();
        services.AddScoped<IJobExecutor, VerificationJobExecutor>();
        return services;
    }
}
