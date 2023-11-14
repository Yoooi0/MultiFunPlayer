using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

public class MouseButtonGesture(MouseButtonGestureDescriptor descriptor) : ISimpleInputGesture
{
    public MouseButton Button => descriptor.Button;
    public IInputGestureDescriptor Descriptor => descriptor;

    public override string ToString() => $"[Mouse Button: {Button}]";

    public static MouseButtonGesture Create(MouseButton button) => new(new(button));
}
