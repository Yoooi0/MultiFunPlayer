using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public interface IVideoSource : IConnectable, IDisposable
    {
        string Name { get; }
        bool ContentVisible { get; set; }
    }
}
