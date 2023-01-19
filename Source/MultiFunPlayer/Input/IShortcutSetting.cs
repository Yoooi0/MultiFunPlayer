using MultiFunPlayer.Common;
using Newtonsoft.Json;
using PropertyChanged;
using System.Collections;

namespace MultiFunPlayer.Input;

public interface IShortcutSetting
{
    object Value { get; set; }
    string Label { get; init; }
    string Description { get; init; }
    string StringFormat { get; init; }
}

public interface IOneOfShortcutSetting : IShortcutSetting
{
    IEnumerable ItemsSource { get; init; }
}

public interface IShortcutSetting<T> : IShortcutSetting
{
    object IShortcutSetting.Value
    {
        get => Value;
        set => Value = (T)value;
    }

    new T Value { get; set; }
}

public interface IOneOfShortcutSetting<T> : IShortcutSetting<T>, IOneOfShortcutSetting
{
    IEnumerable IOneOfShortcutSetting.ItemsSource
    {
        get => ItemsSource;
        init => ItemsSource = new ObservableConcurrentCollection<T>(value.Cast<T>());
    }

    new IEnumerable<T> ItemsSource { get; init; }
}

[AddINotifyPropertyChangedInterface]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutSetting<T> : IShortcutSetting<T>
{
    [JsonProperty] public T Value { get; set; }
    public string Label { get; init; }
    public string Description { get; init; }
    public string StringFormat { get; init; }
}

[AddINotifyPropertyChangedInterface]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class OneOfShortcutSetting<T> : IOneOfShortcutSetting<T>
{
    [JsonProperty] public T Value { get; set; }
    public string Label { get; init; }
    public string Description { get; init; }
    public IEnumerable<T> ItemsSource { get; init; }
    public string StringFormat { get; init; }
}