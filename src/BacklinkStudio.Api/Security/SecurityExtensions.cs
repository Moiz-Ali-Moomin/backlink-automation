using BacklinkStudio.Application;
using Microsoft.AspNetCore.Authentication;

namespace BacklinkStudio.Api.Security;

public static class SecurityExtensions
{
    public static IServiceCollection AddBacklinkStudioSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(ApiKeyAuthenticationHandler.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.AuthenticationScheme, _ => { });

        var authorization = services.AddAuthorizationBuilder();
        foreach (var scope in AuthorizationScopes.All)
        {
            authorization.AddPolicy(scope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasClaim("scope", scope) || context.User.HasClaim("scope", AuthorizationScopes.Admin)));
        }

        return services;
    }
}
