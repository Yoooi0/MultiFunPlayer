using PropertyChanged;
using System.Collections;

namespace MultiFunPlayer.Shortcut;

internal interface IShortcutSetting
{
    object Value { get; set; }
    string Label { get; init; }
    string Description { get; init; }
    string StringFormat { get; init; }
    string TemplateName { get; init; }

    Type Type { get; }
}

internal interface IOneOfShortcutSetting : IShortcutSetting
{
    IEnumerable ItemsSource { get; init; }
}

internal interface IShortcutSetting<T> : IShortcutSetting
{
    object IShortcutSetting.Value
    {
        get => Value;
        set => Value = (T)value;
    }

    new T Value { get; set; }
    Func<T, string> CustomToString { get; init; }
}

internal interface IOneOfShortcutSetting<T> : IShortcutSetting<T>, IOneOfShortcutSetting
{
    IEnumerable IOneOfShortcutSetting.ItemsSource
    {
        get => ItemsSource;
        init => ItemsSource = value as IEnumerable<T>;
    }

    new IEnumerable<T> ItemsSource { get; init; }
}

[AddINotifyPropertyChangedInterface]
internal partial class ShortcutSetting<T> : IShortcutSetting<T>
{
    public T Value { get; set; }
    public string Label { get; init; }
    public string Description { get; init; }
    public string StringFormat { get; init; }
    public string TemplateName { get; init; }
    public Func<T, string> CustomToString { get; init; }

    public Type Type => typeof(T).IsValueType || Value == null ? typeof(T) : Value.GetType();

    public override string ToString() => CustomToString?.Invoke(Value) ?? Value?.ToString() ?? "null";
}

internal sealed class OneOfShortcutSetting<T> : ShortcutSetting<T>, IOneOfShortcutSetting<T>
{
    public IEnumerable<T> ItemsSource { get; init; }
}