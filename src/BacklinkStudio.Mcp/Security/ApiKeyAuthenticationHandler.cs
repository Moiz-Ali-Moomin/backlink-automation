using System.Security.Claims;
using System.Text.Encodings.Web;
using BacklinkStudio.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Mcp.Security;

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
        var key = Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            var authorization = Request.Headers.Authorization.ToString();
            key = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key))
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
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme)), AuthenticationScheme));
    }
}
