using System.Net;
using System.Net.Sockets;

namespace BacklinkStudio.Infrastructure.Http;

internal static class OwnedNetworkSafeNetworkHandler
{
    public static SocketsHttpHandler Create(SubmissionSourceHttpOptions options)
        => Create(options.AllowedPrivateHosts);

    public static SocketsHttpHandler Create(VerificationHttpOptions options)
        => Create(options.AllowedPrivateHosts);

    private static SocketsHttpHandler Create(IEnumerable<string> allowedPrivateHosts)
    {
        var privateHosts = allowedPrivateHosts
            .Select(x => new UriBuilder(Uri.UriSchemeHttps, x.Trim().TrimEnd('.')).Uri.IdnHost.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
            MaxResponseHeadersLength = 32,
            ConnectCallback = (context, cancellationToken) => ConnectAsync(context, privateHosts, cancellationToken)
        };
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, HashSet<string> privateHosts, CancellationToken cancellationToken)
    {
        var normalizedHost = new UriBuilder(Uri.UriSchemeHttps, context.DnsEndPoint.Host).Uri.IdnHost.ToLowerInvariant();
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var address = privateHosts.Contains(normalizedHost)
            ? addresses.FirstOrDefault(IsControlledAddress)
            : addresses.FirstOrDefault(SafeNetworkHandler.IsPublic);
        if (address is null)
            throw new HttpRequestException("Destination does not resolve to an address allowed by the selected network transport policy.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsControlledAddress(IPAddress address) =>
        !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any) &&
        !address.IsIPv6Multicast && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
}
