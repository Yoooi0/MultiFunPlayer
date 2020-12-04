using System;

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
        void Start();
        void Stop();
    }
}
