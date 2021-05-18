using System;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget
{
    public interface IOutputTarget : IConnectable, IDisposable
    {
        string Name { get; }
        bool ContentVisible { get; set; }
    }
}
