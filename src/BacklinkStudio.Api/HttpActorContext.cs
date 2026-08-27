using System.Security.Claims;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Api;

public static class HttpActorContext
{
    public static ActorContext From(HttpContext context)
    {
        var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        Guid? credentialId = Guid.TryParse(context.User.FindFirstValue("credential_id"), out var value) ? value : null;
        return new ActorContext(ActorType.Agent, actorId, credentialId, context.TraceIdentifier, context.Connection.RemoteIpAddress?.ToString());
    }

    public static string IdempotencyKey(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
}
