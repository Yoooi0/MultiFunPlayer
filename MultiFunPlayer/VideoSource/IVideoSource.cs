using MultiFunPlayer.Common;
using System;

namespace MultiFunPlayer.VideoSource
{
    public interface IVideoSource : IConnectable, IDisposable
    {
        string Name { get; }
    }
}
