using Linearstar.Windows.RawInput;
using MultiFunPlayer.Common.Input.Gesture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common.Input.Processor
{
    public interface IInputProcessor
    {
        IEnumerable<IInputGesture> GetGestures(RawInputData data);
    }
}
