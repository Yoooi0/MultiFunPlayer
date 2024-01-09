namespace MultiFunPlayer.Input.TCode;

internal sealed record TCodeButtonGestureDescriptor(string Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[TCode Button: {Button}]";
}

internal sealed class TCodeButtonGesture(TCodeButtonGestureDescriptor descriptor, bool state) : AbstractSimpleInputGesture(descriptor, state)
{
    public string Button => descriptor.Button;

    public override string ToString() => $"[TCode Button: {Button}, State: {State}]";

    public static TCodeButtonGesture Create(string button, bool state) => new(new(button), state);
}