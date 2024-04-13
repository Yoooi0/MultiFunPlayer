namespace MultiFunPlayer.Common;

internal enum ConnectionStatus
{
    Disconnected,
    Disconnecting,
    Connecting,
    Connected
}

internal enum ConnectionType
{
    Manual,
    AutoConnect
}

internal interface IConnectable
{
    ConnectionStatus Status { get; }
    bool AutoConnectEnabled { get; }

    Task ConnectAsync(ConnectionType connectionType);
    Task DisconnectAsync();
    Task WaitForStatus(IEnumerable<ConnectionStatus> statuses, CancellationToken token);
    Task WaitForStatus(IEnumerable<ConnectionStatus> statuses) => WaitForStatus(statuses, CancellationToken.None);
}