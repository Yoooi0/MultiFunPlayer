namespace MultiFunPlayer.Input.TCode;

public sealed record TCodeButtonGestureDescriptor(string Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[TCode Button: {Button}]";
}

public sealed class TCodeButtonGesture(TCodeButtonGestureDescriptor descriptor, bool state) : AbstractSimpleInputGesture(descriptor, state)
{
    public string Button => descriptor.Button;

    public override string ToString() => $"[TCode Button: {Button}, State: {state}]";

    public static TCodeButtonGesture Create(string button, bool state) => new(new(button), state);
}