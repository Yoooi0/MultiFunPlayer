using System.ComponentModel;

namespace MultiFunPlayer.Input.Shortcut;

[DisplayName("Button Release")]
public sealed class ReleaseShortcut(ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(gesture)
{
    private bool _lastPressed;

    public bool HandleRepeating { get; set; } = false;

    protected override ISimpleInputGestureData CreateData(ISimpleInputGesture gesture)
    {
        var wasReleased = _lastPressed && !gesture.State;
        _lastPressed = gesture.State;
        if (gesture.State)
            return null;

        return (HandleRepeating || wasReleased) ? SimpleInputGestureData.FromGesture(gesture) : null;
    }
}