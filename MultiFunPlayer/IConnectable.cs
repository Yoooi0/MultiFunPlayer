using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer
{
    public enum ConnectionStatus
    {
        Disconnected,
        Disconnecting,
        Connecting,
        Connected
    }

    public interface IConnectable
    {
        ConnectionStatus Status { get; }
        bool AutoConnectEnabled { get; set; }

        Task ConnectAsync();
        Task DisconnectAsync();
        ValueTask<bool> CanConnectAsync(CancellationToken token);
        ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token);
        Task WaitForStatus(IEnumerable<ConnectionStatus> statuses, CancellationToken token);
    }
}
