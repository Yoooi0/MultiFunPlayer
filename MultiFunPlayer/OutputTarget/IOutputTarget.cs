using MultiFunPlayer.Common;

namespace MultiFunPlayer.OutputTarget;

public interface IOutputTarget : IConnectable, IDisposable
{
    string Name { get; }
}
