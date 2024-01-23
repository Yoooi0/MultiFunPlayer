using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Common;

public static partial class NetUtils
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

        var match = EndpointRegex().Match(endpointString);
        if (!match.Success)
            return null;

        var ipOrHost = match.Groups["ipOrHost"].Value;
        var port = int.Parse(match.Groups["port"].Value);

        if (match.Groups["family"].Success)
            return new DnsEndPoint(ipOrHost, port);

        return Uri.CheckHostName(ipOrHost) switch
        {
            UriHostNameType.IPv4 or UriHostNameType.IPv6 when IPAddress.TryParse(ipOrHost, out var ipAddress) => new IPEndPoint(ipAddress, port),
            UriHostNameType.Dns => new DnsEndPoint(ipOrHost, port),
            _ => null
        };
    }

    public static bool TryParseEndpoint(string endpointString, out EndPoint endpoint)
    {
        endpoint = ParseEndpoint(endpointString);
        return endpoint != null;
    }

    [GeneratedRegex(@"^(?:(?<family>InterNetwork|InterNetworkV6|Unspecified)\/)?(?<ipOrHost>.+):(?<port>\d+)$")]
    private static partial Regex EndpointRegex();
}
