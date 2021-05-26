using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common.Input.Gesture;
using System.Collections.Generic;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Processor
{
    public class MouseInputProcessor : IInputProcessor
    {
        private float xAxis, yAxis;
        private float wAxis, whAxis;

        public MouseInputProcessor()
        {
            xAxis = yAxis = 0.5f;
            wAxis = whAxis = 0.5f;
        }

        public IEnumerable<IInputGesture> GetGestures(RawInputData data)
        {
            if (data is not RawInputMouseData mouse)
                yield break;

            bool HasFlag(RawMouseButtonFlags flag) => mouse.Mouse.Buttons.HasFlag(flag);

            if (HasFlag(RawMouseButtonFlags.Button4Down)) yield return new MouseButtonGesture(MouseButton.XButton1);
            else if (HasFlag(RawMouseButtonFlags.Button5Down)) yield return new MouseButtonGesture(MouseButton.XButton2);
            else if (HasFlag(RawMouseButtonFlags.LeftButtonDown)) yield return new MouseButtonGesture(MouseButton.Left);
            else if (HasFlag(RawMouseButtonFlags.RightButtonDown)) yield return new MouseButtonGesture(MouseButton.Right);
            else if (HasFlag(RawMouseButtonFlags.MiddleButtonDown)) yield return new MouseButtonGesture(MouseButton.Middle);

            if (mouse.Mouse.LastX != 0)
            {
                var delta = mouse.Mouse.LastX / 500.0f;
                xAxis = MathUtils.Clamp01(xAxis + delta);
                yield return new MouseAxisGesture(MouseAxis.X, xAxis, delta);
            }

            if (mouse.Mouse.LastY != 0)
            {
                var delta = mouse.Mouse.LastY / 500.0f;
                yAxis = MathUtils.Clamp01(yAxis + delta);
                yield return new MouseAxisGesture(MouseAxis.Y, yAxis, delta);
            }

            if (mouse.Mouse.ButtonData != 0)
            {
                var delta = mouse.Mouse.ButtonData / (120.0f * 50.0f);
                if (HasFlag(RawMouseButtonFlags.MouseWheel))
                {
                    wAxis = MathUtils.Clamp01(wAxis + delta);
                    yield return new MouseAxisGesture(MouseAxis.MouseWheel, wAxis, delta);
                }
                else if (HasFlag(RawMouseButtonFlags.MouseHorizontalWheel))
                {
                    whAxis = MathUtils.Clamp01(whAxis + delta);
                    yield return new MouseAxisGesture(MouseAxis.MouseHorizontalWheel, whAxis, delta);
                }
            }
        }
    }
}
