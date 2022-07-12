using System.Net;
using System.Net.Http;

namespace MultiFunPlayer.Common;

public static class NetUtils
{
    private static readonly SocketsHttpHandler _handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        MaxConnectionsPerServer = 5
    };

    public static HttpClient CreateHttpClient() => new(_handler, disposeHandler: false);

    public static EndPoint ParseEndpoint(string endpointString)
    {
        if (string.IsNullOrWhiteSpace(endpointString))
            return null;

        var parts = endpointString.Split(':');
        if (parts.Length != 2)
            return null;

        var hostOrIPAddress = parts[0];
        if (!int.TryParse(parts[1], out var port))
            return null;

        return Uri.CheckHostName(hostOrIPAddress) switch
        {
            UriHostNameType.IPv4 or UriHostNameType.IPv6 when IPAddress.TryParse(hostOrIPAddress, out var ipAddress) => new IPEndPoint(ipAddress, port),
            UriHostNameType.Dns => new DnsEndPoint(hostOrIPAddress, port),
            _ => default(EndPoint)
        };
    }

    public static bool TryParseEndpoint(string endpointString, out EndPoint endpoint)
    {
        endpoint = ParseEndpoint(endpointString);
        return endpoint != null;
    }
}
