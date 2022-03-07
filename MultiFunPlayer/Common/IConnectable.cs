namespace MultiFunPlayer.Common;

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
    bool AutoConnectEnabled { get; }

    Task ConnectAsync();
    Task DisconnectAsync();
    ValueTask<bool> CanConnectAsync(CancellationToken token);
    ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token);
    Task WaitForStatus(IEnumerable<ConnectionStatus> statuses, CancellationToken token);
    Task WaitForStatus(IEnumerable<ConnectionStatus> statuses) => WaitForStatus(statuses, CancellationToken.None);
}
