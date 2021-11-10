using MultiFunPlayer.Common;

namespace MultiFunPlayer.VideoSource;

public interface IVideoSource : IConnectable, IDisposable
{
    string Name { get; }
}
