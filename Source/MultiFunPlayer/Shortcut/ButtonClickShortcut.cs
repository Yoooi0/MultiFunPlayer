﻿using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Button Click")]
internal sealed class ButtonClickShortcut(IShortcutActionResolver actionResolver, ISimpleInputGestureDescriptor gesture)
    : AbstractShortcut<ISimpleInputGesture, ISimpleInputGestureData>(actionResolver, gesture)
{
    private int _stateCounter;

    public int ClickCount { get; set; } = 2;
    public int MaximumClickInterval { get; set; } = 200;

    protected override void Update(ISimpleInputGesture gesture)
    {
        if (gesture.State && _stateCounter % 2 == 0)
        {
            _stateCounter++;
            CancelDelay();
        }
        else if (!gesture.State && _stateCounter % 2 == 1)
        {
            _stateCounter++;

            Delay(MaximumClickInterval, () => {
                if (_stateCounter == 2 * ClickCount)
                    Invoke(SimpleInputGestureData.Default);

                _stateCounter = 0;
            });
        }
    }
}