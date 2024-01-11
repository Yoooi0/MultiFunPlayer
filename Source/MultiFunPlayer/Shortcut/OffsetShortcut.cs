using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using System.ComponentModel;
using System.Text;

namespace MultiFunPlayer.Shortcut;

[DisplayName("Axis Offset")]
internal sealed class OffsetShortcut(IShortcutActionResolver actionResolver, IAxisInputGestureDescriptor gesture)
    : AbstractShortcut<IAxisInputGesture, IAxisInputGestureData>(actionResolver, gesture)
{
    private const double DeltaTime = 0.1;
    private double _offset;

    public OffsetShortcutMode OffsetMode { get; set; } = OffsetShortcutMode.AbsoluteJoystick;
    public double Deadzone { get; set; } = 0.05;
    public double Speed { get; set; } = 0.1;
    public bool Invert { get; set; } = false;

    protected override void Update(IAxisInputGesture gesture)
    {
        var sign = Invert ? -1 : 1;
        _offset = (OffsetMode, gesture.Value) switch
        {
            (OffsetShortcutMode.Absolute, double v) when v >= Deadzone => MathUtils.Map(gesture.Value, Deadzone, 1, 0, 1) * Speed * sign,
            (OffsetShortcutMode.AbsoluteJoystick, double v) when v >= 0.5 + Deadzone => MathUtils.Map(v, 0.5 + Deadzone, 1, 0, 1) * Speed * sign,
            (OffsetShortcutMode.AbsoluteJoystick, double v) when v <= 0.5 - Deadzone => MathUtils.Map(v, 0.5 - Deadzone, 0, 0, -1) * Speed * sign,
            _ => 0
        };

        if (_offset == 0)
            CancelRepeat();
        else if (!IsRepeatRunning())
            Repeat(TimeSpan.FromSeconds(DeltaTime), InvokeOffset);
    }

    private void InvokeOffset() => Invoke(new AxisInputGestureData(_offset, DeltaTime, false));

    protected override void PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        PrintProperty(builder, () => OffsetMode);
        PrintProperty(builder, () => Deadzone);
        PrintProperty(builder, () => Speed);
        PrintProperty(builder, () => Invert);
    }
}

public enum OffsetShortcutMode
{
    Absolute,
    AbsoluteJoystick
}