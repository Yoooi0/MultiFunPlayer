using System.Net.Http;

namespace MultiFunPlayer.Common;

public static class WebUtils
{
    private static readonly SocketsHttpHandler _handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        MaxConnectionsPerServer = 5
    };

    public static HttpClient CreateClient() => new(_handler, disposeHandler: false);
}
