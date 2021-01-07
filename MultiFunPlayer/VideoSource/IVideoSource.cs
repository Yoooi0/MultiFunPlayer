using System;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public enum VideoSourceStatus
    {
        Disconnected,
        Connected
    }

    public interface IVideoSource : IDisposable
    {
        string Name { get; }
        VideoSourceStatus Status { get; }
        Task StartAsync();
        Task StopAsync();
    }
}
