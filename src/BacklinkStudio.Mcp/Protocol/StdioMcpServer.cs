using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Mcp.Protocol;

public static class StdioMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(IServiceProvider services, IReadOnlySet<string> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            McpResponse? response;
            try
            {
                var request = JsonSerializer.Deserialize<McpRequest>(line, JsonOptions) ?? throw new JsonException("Empty request.");
                await using var scope = services.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<McpToolDispatcher>();
                response = await dispatcher.DispatchAsync(
                    request,
                    new ActorContext(ActorType.Agent, "local-stdio", null, request.Id?.ToString() ?? Guid.CreateVersion7().ToString("D"), null),
                    scopes,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                response = McpResponse.Failure(null, -32700, "Parse error", exception.Message);
            }

            if (response is not null)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
                await Console.Out.FlushAsync(cancellationToken);
            }
        }
    }
}
