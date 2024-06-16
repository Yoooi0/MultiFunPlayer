using MahApps.Metro.Controls;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutSettingTemplateContext
{
    string StringFormat { get; }

    object CoerceValue(object value);
}

internal sealed record NumericUpDownShortcutSettingTemplateContext(
    double Minimum, double Maximum, double Interval, string StringFormat, NumericInput NumericInput)
    : IShortcutSettingTemplateContext
{
    public object CoerceValue(object value)
    {
        if (value is int i)
            return (int)Math.Clamp(i, Minimum, Maximum);
        if (value is double d)
            return Math.Clamp(d, Minimum, Maximum);
        return value;
    }
}
