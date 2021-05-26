using Linearstar.Windows.RawInput;
using MultiFunPlayer.Common.Input.Gesture;
using System.Collections.Generic;

namespace MultiFunPlayer.Common.Input.Processor
{
    public interface IInputProcessor
    {
        IEnumerable<IInputGesture> GetGestures(RawInputData data);
    }
}
