using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public interface IInputGesture : IEquatable<IInputGesture> { }
    public interface IAxisInputGesture : IInputGesture
    {
        float Value { get; }
        float Delta { get; }
    }
}
