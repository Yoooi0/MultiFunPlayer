using System;

namespace MultiFunPlayer.Player
{
    public enum VideoPlayerStatus
    {
        Disconnected,
        Connected
    }

    public interface IVideoPlayer : IDisposable
    {
        string Name { get; }
        VideoPlayerStatus Status { get; }
        void Start();
    }
}
