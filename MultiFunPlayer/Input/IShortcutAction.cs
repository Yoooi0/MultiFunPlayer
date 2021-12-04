using Newtonsoft.Json;
using PropertyChanged;
using System.ComponentModel;

namespace MultiFunPlayer.Input;

public interface IShortcutAction
{
    IShortcutActionDescriptor Descriptor { get; }
    IEnumerable<IShortcutSetting> Settings { get; }
    void Invoke(IInputGesture gesture);
}

[AddINotifyPropertyChangedInterface]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutAction : IShortcutAction
{
    private readonly Action<IInputGesture> _action;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public IEnumerable<IShortcutSetting> Settings { get; }

    public string DisplayName => $"{Descriptor.Name}";

    [JsonConstructor]
    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<IInputGesture> action)
    {
        _action = action;
        Descriptor = descriptor;
        Settings = Array.Empty<IShortcutSetting>();
    }

    public void Invoke(IInputGesture gesture) => _action?.Invoke(gesture);
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutAction<T0> : IShortcutAction, INotifyPropertyChanged
{
    private readonly Action<IInputGesture, T0> _action;
    private readonly IShortcutSetting<T0> _setting0;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public IEnumerable<IShortcutSetting> Settings
        => Enumerable.Empty<IShortcutSetting>().Append(_setting0);

    public string DisplayName => $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<IInputGesture, T0> action, IShortcutSetting<T0> setting0)
    {
        _action = action;
        Descriptor = descriptor;

        _setting0 = setting0;

        foreach (var setting in Settings.OfType<INotifyPropertyChanged>())
            setting.PropertyChanged += OnSettingsValueChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsValueChanged(object sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public void Invoke(IInputGesture gesture) => _action?.Invoke(gesture, _setting0.Value);

    public event PropertyChangedEventHandler PropertyChanged;
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutAction<T0, T1> : IShortcutAction, INotifyPropertyChanged
{
    private readonly Action<IInputGesture, T0, T1> _action;
    private readonly IShortcutSetting<T0> _setting0;
    private readonly IShortcutSetting<T1> _setting1;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public IEnumerable<IShortcutSetting> Settings
        => Enumerable.Empty<IShortcutSetting>().Append(_setting0).Append(_setting1);

    public string DisplayName => $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<IInputGesture, T0, T1> action, IShortcutSetting<T0> setting0, IShortcutSetting<T1> setting1)
    {
        _action = action;
        Descriptor = descriptor;

        _setting0 = setting0;
        _setting1 = setting1;

        foreach (var setting in Settings.OfType<INotifyPropertyChanged>())
            setting.PropertyChanged += OnSettingsValueChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsValueChanged(object sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public void Invoke(IInputGesture gesture) => _action?.Invoke(gesture, _setting0.Value, _setting1.Value);

    public event PropertyChangedEventHandler PropertyChanged;
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutAction<T0, T1, T2> : IShortcutAction, INotifyPropertyChanged
{
    private readonly Action<IInputGesture, T0, T1, T2> _action;
    private readonly IShortcutSetting<T0> _setting0;
    private readonly IShortcutSetting<T1> _setting1;
    private readonly IShortcutSetting<T2> _setting2;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public IEnumerable<IShortcutSetting> Settings
        => Enumerable.Empty<IShortcutSetting>().Append(_setting0).Append(_setting1).Append(_setting2);

    public string DisplayName => $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<IInputGesture, T0, T1, T2> action, IShortcutSetting<T0> setting0, IShortcutSetting<T1> setting1, IShortcutSetting<T2> setting2)
    {
        _action = action;
        Descriptor = descriptor;

        _setting0 = setting0;
        _setting1 = setting1;
        _setting2 = setting2;

        foreach (var setting in Settings.OfType<INotifyPropertyChanged>())
            setting.PropertyChanged += OnSettingsValueChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsValueChanged(object sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public void Invoke(IInputGesture gesture) => _action?.Invoke(gesture, _setting0.Value, _setting1.Value, _setting2.Value);

    public event PropertyChangedEventHandler PropertyChanged;
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutAction<T0, T1, T2, T3> : IShortcutAction, INotifyPropertyChanged
{
    private readonly Action<IInputGesture, T0, T1, T2, T3> _action;
    private readonly IShortcutSetting<T0> _setting0;
    private readonly IShortcutSetting<T1> _setting1;
    private readonly IShortcutSetting<T2> _setting2;
    private readonly IShortcutSetting<T3> _setting3;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)]
    public IEnumerable<IShortcutSetting> Settings
        => Enumerable.Empty<IShortcutSetting>().Append(_setting0).Append(_setting1).Append(_setting2).Append(_setting3);

    public string DisplayName => $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

    public ShortcutAction(IShortcutActionDescriptor descriptor, Action<IInputGesture, T0, T1, T2, T3> action, IShortcutSetting<T0> setting0, IShortcutSetting<T1> setting1, IShortcutSetting<T2> setting2, IShortcutSetting<T3> setting3)
    {
        _action = action;
        Descriptor = descriptor;

        _setting0 = setting0;
        _setting1 = setting1;
        _setting2 = setting2;
        _setting3 = setting3;

        foreach (var setting in Settings.OfType<INotifyPropertyChanged>())
            setting.PropertyChanged += OnSettingsValueChanged;
    }

    [SuppressPropertyChangedWarnings]
    private void OnSettingsValueChanged(object sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public void Invoke(IInputGesture gesture) => _action?.Invoke(gesture, _setting0.Value, _setting1.Value, _setting2.Value, _setting3.Value);

    public event PropertyChangedEventHandler PropertyChanged;
}