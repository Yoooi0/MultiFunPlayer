﻿using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Offset")]
internal sealed class AxisOffsetShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(actionResolver, gesture)
{
    private const double DeltaTime = 0.1;
    private double _offset;

    public AxisOffsetShortcutMode OffsetMode { get; set; } = AxisOffsetShortcutMode.AbsoluteJoystick;
    public double Speed { get; set; } = 0.1;
    public bool Invert { get; set; } = false;

    protected override void Update(IAxisInputGesture gesture)
    {
        var sign = Invert ? -1 : 1;
        _offset = (OffsetMode, gesture.Value) switch
        {
            (AxisOffsetShortcutMode.Absolute, double v) => gesture.Value * Speed * sign,
            (AxisOffsetShortcutMode.AbsoluteJoystick, double v) when v >= 0.5 => MathUtils.Map(v, 0.5, 1, 0, 1) * Speed * sign,
            (AxisOffsetShortcutMode.AbsoluteJoystick, double v) when v <= 0.5 => MathUtils.Map(v, 0.5, 0, 0, -1) * Speed * sign,
            _ => 0
        };

        if (_offset == 0)
            CancelRepeat();
        else if (!IsRepeatRunning())
            Repeat(TimeSpan.FromSeconds(DeltaTime), InvokeOffset);
    }

    private void InvokeOffset() => Invoke(AxisInputGestureData.Relative(_offset, DeltaTime));
}

internal enum AxisOffsetShortcutMode
{
    Absolute,
    AbsoluteJoystick
}