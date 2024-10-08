﻿using MultiFunPlayer.Input;
using System.ComponentModel;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Press")]
internal sealed class ButtonPressShortcut(IShortcutActionRunner actionRunner, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionRunner, gesture)
{
    private bool _lastPressed;

    public bool HandleRepeating { get; set; } = false;

    protected override void Update(ISimpleInputGesture gesture)
    {
        var wasPressed = !_lastPressed && gesture.State;
        _lastPressed = gesture.State;
        if (!gesture.State)
            return;

        if (HandleRepeating || wasPressed)
            Invoke(SimpleInputGestureData.Default);
    }
}