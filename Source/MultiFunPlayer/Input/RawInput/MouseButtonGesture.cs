using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

internal sealed record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

internal sealed class MouseButtonGesture(MouseButtonGestureDescriptor descriptor, bool state) : AbstractSimpleInputGesture(descriptor, state)
{
    public MouseButton Button => descriptor.Button;

    public override string ToString() => $"[Mouse Button: {Button}, State: {State}]";

    public static MouseButtonGesture Create(MouseButton button, bool state) => new(new(button), state);
}
