using MahApps.Metro.Controls;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutSettingTemplateContext
{
    string StringFormat { get; }
}

internal sealed record NumericUpDownShortcutSettingTemplateContext(
    double Minimum, double Maximum, double Interval, string StringFormat, NumericInput NumericInput)
    : IShortcutSettingTemplateContext;