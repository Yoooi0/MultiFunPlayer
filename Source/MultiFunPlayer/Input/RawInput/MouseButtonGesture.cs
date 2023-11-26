using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public sealed record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

public sealed class MouseButtonGesture(MouseButtonGestureDescriptor descriptor) : AbstractSimpleInputGesture(descriptor)
{
    public MouseButton Button => descriptor.Button;

    public override string ToString() => $"[Mouse Button: {Button}]";

    public static MouseButtonGesture Create(MouseButton button) => new(new(button));
}
