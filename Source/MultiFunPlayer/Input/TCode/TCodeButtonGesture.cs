namespace MultiFunPlayer.Input.TCode;

public record TCodeButtonGestureDescriptor(string Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[TCode Button: {Button}]";
}

public class TCodeButtonGesture(TCodeButtonGestureDescriptor descriptor) : ISimpleInputGesture
{
    public string Button => descriptor.Button;
    public IInputGestureDescriptor Descriptor => descriptor;

    public override string ToString() => $"[TCode Button: {Button}]";

    public static TCodeButtonGesture Create(string button) => new(new(button));
}