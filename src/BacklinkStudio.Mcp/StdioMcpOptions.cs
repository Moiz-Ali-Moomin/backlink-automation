using BacklinkStudio.Application;

namespace BacklinkStudio.Mcp;

/// <summary>
/// Explicit configuration for the locally attached STDIO transport.
/// STDIO never inherits administrator authority: absent configuration resolves to
/// <see cref="AuthorizationScopes.DefaultStdio"/>, a read-only least-privilege set.
/// </summary>
public sealed class StdioMcpOptions
{
    public const string SectionName = "McpStdio";

    /// <summary>
    /// Scopes granted to the STDIO client. When null or empty the least-privilege default applies.
    /// Credential-management scopes are honoured only when both listed here and
    /// <see cref="AllowCredentialManagement"/> is set, so that elevation is always deliberate.
    /// </summary>
    public string[]? Scopes { get; init; }

    /// <summary>
    /// Must be explicitly enabled before a credential-management scope listed in <see cref="Scopes"/>
    /// is granted. Guards against a stray configuration entry silently creating an administrator transport.
    /// </summary>
    public bool AllowCredentialManagement { get; init; }

    public IReadOnlySet<string> Resolve() => Resolve(Scopes, AllowCredentialManagement);

    public static IReadOnlySet<string> Resolve(IReadOnlyList<string>? configured, bool allowCredentialManagement)
    {
        var requested = configured?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [];
        if (requested.Length == 0)
        {
            return AuthorizationScopes.DefaultStdio.ToHashSet(StringComparer.Ordinal);
        }

        if (requested.Length > AuthorizationScopes.All.Length)
        {
            throw new InvalidOperationException("The STDIO scope list contains more entries than there are known scopes.");
        }

        var unknown = requested.Where(x => !AuthorizationScopes.IsKnown(x)).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException($"Unknown STDIO scope(s): {string.Join(", ", unknown.Order(StringComparer.Ordinal))}.");
        }

        var elevated = requested.Where(AuthorizationScopes.IsCredentialManagement).ToArray();
        if (elevated.Length > 0 && !allowCredentialManagement)
        {
            throw new InvalidOperationException(
                $"STDIO scope(s) {string.Join(", ", elevated.Order(StringComparer.Ordinal))} manage agent credentials and require {SectionName}:AllowCredentialManagement=true.");
        }

        return requested.ToHashSet(StringComparer.Ordinal);
    }
}
