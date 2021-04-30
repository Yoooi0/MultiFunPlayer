using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public enum VideoSourceStatus
    {
        Disconnected,
        Disconnecting,
        Connecting,
        Connected
    }

    public interface IVideoSource : IDisposable
    {
        string Name { get; }
        VideoSourceStatus Status { get; }
        bool ContentVisible { get; set; }
        bool AutoConnectEnabled { get; set; }

        Task ConnectAsync();
        Task DisconnectAsync();
        ValueTask<bool> CanConnectAsync(CancellationToken token);
        ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token);
        Task WaitForStatus(IEnumerable<VideoSourceStatus> statuses, int checkFrequency, CancellationToken token);
    }
}
