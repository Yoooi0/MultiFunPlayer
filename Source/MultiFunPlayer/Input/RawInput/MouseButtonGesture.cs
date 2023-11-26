using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

public class MouseButtonGesture(MouseButtonGestureDescriptor descriptor) : AbstractSimpleInputGesture(descriptor)
{
    public MouseButton Button => descriptor.Button;

    public override string ToString() => $"[Mouse Button: {Button}]";

    public static MouseButtonGesture Create(MouseButton button) => new(new(button));
}
