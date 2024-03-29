﻿using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Press")]
internal sealed class ButtonPressShortcut(IShortcutActionResolver actionResolver, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
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