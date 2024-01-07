using System.ComponentModel;

namespace MultiFunPlayer.Input.Binding;

[DisplayName("Button Press")]
public sealed class PressShortcut(ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(gesture)
{
    private bool _lastPressed;

    public bool HandleRepeating { get; set; } = false;

    protected override ISimpleInputGestureData CreateData(ISimpleInputGesture gesture)
    {
        var wasPressed = !_lastPressed && gesture.State;
        _lastPressed = gesture.State;
        if (!gesture.State)
            return null;

        return (HandleRepeating || wasPressed) ? SimpleInputGestureData.FromGesture(gesture) : null;
    }
}