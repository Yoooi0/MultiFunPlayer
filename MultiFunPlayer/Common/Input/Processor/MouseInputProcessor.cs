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

            if (HasFlag(RawMouseButtonFlags.Button4Down)) yield return MouseButtonGesture.Create(MouseButton.XButton1);
            else if (HasFlag(RawMouseButtonFlags.Button5Down)) yield return MouseButtonGesture.Create(MouseButton.XButton2);
            else if (HasFlag(RawMouseButtonFlags.LeftButtonDown)) yield return MouseButtonGesture.Create(MouseButton.Left);
            else if (HasFlag(RawMouseButtonFlags.RightButtonDown)) yield return MouseButtonGesture.Create(MouseButton.Right);
            else if (HasFlag(RawMouseButtonFlags.MiddleButtonDown)) yield return MouseButtonGesture.Create(MouseButton.Middle);

            if (mouse.Mouse.LastX != 0)
            {
                var delta = mouse.Mouse.LastX / 500.0f;
                xAxis = MathUtils.Clamp01(xAxis + delta);
                yield return MouseAxisGesture.Create(MouseAxis.X, xAxis, delta);
            }

            if (mouse.Mouse.LastY != 0)
            {
                var delta = mouse.Mouse.LastY / 500.0f;
                yAxis = MathUtils.Clamp01(yAxis + delta);
                yield return MouseAxisGesture.Create(MouseAxis.Y, yAxis, delta);
            }

            if (mouse.Mouse.ButtonData != 0)
            {
                var delta = mouse.Mouse.ButtonData / (120.0f * 50.0f);
                if (HasFlag(RawMouseButtonFlags.MouseWheel))
                {
                    wAxis = MathUtils.Clamp01(wAxis + delta);
                    yield return MouseAxisGesture.Create(MouseAxis.MouseWheel, wAxis, delta);
                }
                else if (HasFlag(RawMouseButtonFlags.MouseHorizontalWheel))
                {
                    whAxis = MathUtils.Clamp01(whAxis + delta);
                    yield return MouseAxisGesture.Create(MouseAxis.MouseHorizontalWheel, whAxis, delta);
                }
            }
        }
    }
}
