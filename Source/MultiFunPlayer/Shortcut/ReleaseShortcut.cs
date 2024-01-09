using MultiFunPlayer.Input;
using System;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Release")]
internal sealed class ReleaseShortcut(IShortcutActionResolver actionResolver, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
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

    protected override void PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        PrintProperty(builder, () => HandleRepeating);
    }
}