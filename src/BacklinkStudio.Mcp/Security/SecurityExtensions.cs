using Microsoft.AspNetCore.Authentication;

namespace BacklinkStudio.Mcp.Security;

public static class SecurityExtensions
{
    public static IServiceCollection AddMcpSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(ApiKeyAuthenticationHandler.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.AuthenticationScheme, _ => { });
        services.AddAuthorizationBuilder().AddPolicy("mcp", policy => policy.RequireAuthenticatedUser());
        return services;
    }
}
