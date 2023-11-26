using MultiFunPlayer.Input.RawInput;

namespace MultiFunPlayer.Input.TCode;

public record TCodeButtonGestureDescriptor(string Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[TCode Button: {Button}]";
}

public class TCodeButtonGesture(TCodeButtonGestureDescriptor descriptor) : AbstractSimpleInputGesture(descriptor)
{
    public string Button => descriptor.Button;

    public override string ToString() => $"[TCode Button: {Button}]";

    public static TCodeButtonGesture Create(string button) => new(new(button));
}