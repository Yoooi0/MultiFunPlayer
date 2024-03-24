using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Release")]
internal sealed class ButtonReleaseShortcut(IShortcutActionRunner actionRunner, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionRunner, gesture)
{
    private bool _lastPressed;

    public bool HandleRepeating { get; set; } = false;

    protected override void Update(ISimpleInputGesture gesture)
    {
        var wasReleased = _lastPressed && !gesture.State;
        _lastPressed = gesture.State;
        if (gesture.State)
            return;

        if (HandleRepeating || wasReleased)
            Invoke(SimpleInputGestureData.Default);
    }
}