namespace MultiFunPlayer.Input.TCode;

public record TCodeButtonGestureDescriptor(string Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[TCode Button: {Button}]";
}

public class TCodeButtonGesture : ISimpleInputGesture
{
    private readonly TCodeButtonGestureDescriptor _descriptor;

    public bool State { get; }
    public string Button => _descriptor.Button;
    public IInputGestureDescriptor Descriptor => _descriptor;

    public TCodeButtonGesture(TCodeButtonGestureDescriptor descriptor, bool state)
    {
        State = state;
        _descriptor = descriptor;
    }

    public override string ToString() => $"[TCode Button: {Button}]";

    public static TCodeButtonGesture Create(string button, bool state) => new(new(button), state);
}