using MultiFunPlayer.Common;

namespace MultiFunPlayer.MediaSource;

public interface IMediaSource : IConnectable, IDisposable
{
    string Name { get; }
}
