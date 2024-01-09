using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public sealed record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

public sealed class MouseButtonGesture(MouseButtonGestureDescriptor descriptor, bool state) : AbstractSimpleInputGesture(descriptor, state)
{
    public MouseButton Button => descriptor.Button;

    public override string ToString() => $"[Mouse Button: {Button}, State: {State}]";

    public static MouseButtonGesture Create(MouseButton button, bool state) => new(new(button), state);
}
