using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public enum VideoSourceStatus
    {
        Disconnecting,
        Disconnected,
        Connecting,
        Connected
    }

    public interface IVideoSource : IDisposable
    {
        string Name { get; }
        VideoSourceStatus Status { get; }
        Task StartAsync();
        Task StopAsync();
        ValueTask<bool> CanStartAsync(CancellationToken token);
        Task WaitForStatus(IEnumerable<VideoSourceStatus> statuses, int checkFrequency, CancellationToken token);
    }
}
