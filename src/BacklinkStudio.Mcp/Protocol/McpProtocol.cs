using System.Text.Json;
using System.Text.Json.Serialization;

namespace BacklinkStudio.Mcp.Protocol;

public sealed record McpRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record McpError(int Code, string Message, object? Data = null);

public sealed record McpResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] McpError? Error)
{
    public static McpResponse Success(JsonElement? id, object? result) => new("2.0", id, result, null);
    public static McpResponse Failure(JsonElement? id, int code, string message, object? data = null) => new("2.0", id, null, new McpError(code, message, data));
}

public sealed record McpTool(string Name, string Description, object InputSchema, string RequiredScope);
