using System.Security.Claims;
using System.Text.Encodings.Web;
using BacklinkStudio.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICredentialAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = ExtractKey(Request);
        if (key is null)
        {
            return AuthenticateResult.NoResult();
        }

        var credential = await authenticator.AuthenticateAsync(key, Context.RequestAborted);
        if (credential is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, credential.UserId.ToString("D")),
            new(ClaimTypes.Name, credential.Name),
            new("credential_id", credential.CredentialId.ToString("D"))
        };
        claims.AddRange(credential.Scopes.Select(scope => new Claim("scope", scope)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationScheme));
    }

    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var apiKey) && apiKey.Count == 1)
        {
            return apiKey[0];
        }

        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }
}
