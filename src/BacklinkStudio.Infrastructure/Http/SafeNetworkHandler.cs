using System.Net;
using System.Net.Sockets;

namespace BacklinkStudio.Infrastructure.Http;

internal static class SafeNetworkHandler
{
    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 4,
            MaxResponseHeadersLength = 32,
            ConnectCallback = ConnectPublicAsync
        };
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublic) ?? throw new HttpRequestException("Destination resolves only to a non-public network address.");
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

    internal static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) != 0xfc &&
                   !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        }

        var octets = address.GetAddressBytes();
        return octets[0] != 0 && octets[0] != 10 && octets[0] != 127 && octets[0] < 224 &&
               !(octets[0] == 100 && octets[1] is >= 64 and <= 127) &&
               !(octets[0] == 169 && octets[1] == 254) &&
               !(octets[0] == 172 && octets[1] is >= 16 and <= 31) &&
               !(octets[0] == 192 && octets[1] == 0 && octets[2] is 0 or 2) &&
               !(octets[0] == 192 && octets[1] == 168) &&
               !(octets[0] == 198 && (octets[1] is 18 or 19 || (octets[1] == 51 && octets[2] == 100))) &&
               !(octets[0] == 203 && octets[1] == 0 && octets[2] == 113);
    }
}
