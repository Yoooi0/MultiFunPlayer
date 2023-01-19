using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;

namespace MultiFunPlayer.Input;

public interface IShortcutActionConfiguration
{
    IShortcutActionDescriptor Descriptor { get; }
    IEnumerable<IShortcutSetting> Settings { get; }

    object[] GetActionParams();
    object[] GetActionParamsWithGesture(IInputGesture gesture);
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ShortcutActionConfiguration : PropertyChangedBase, IShortcutActionConfiguration
{
    private readonly IReadOnlyList<IShortcutSetting> _settings;
    private object[] _valuesBuffer;

    [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }

    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)]
    public IEnumerable<IShortcutSetting> Settings => _settings;

    public ShortcutActionConfiguration(IShortcutActionDescriptor descriptor, params IShortcutSetting[] settings)
    {
        Descriptor = descriptor;

        _settings = new List<IShortcutSetting>(settings);
        foreach (var setting in _settings.OfType<INotifyPropertyChanged>())
            setting.PropertyChanged += (_, _) => NotifyOfPropertyChange(() => DisplayName);
    }

    public string DisplayName => _settings.Count == 0 ? Descriptor.Name : $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

    public object[] GetActionParams()
    {
        EnsureBufferLength(_settings.Count);

        for (var i = 0; i < _settings.Count; i++)
            _valuesBuffer[i] = _settings[i].Value;

        return _valuesBuffer;
    }

    public object[] GetActionParamsWithGesture(IInputGesture gesture)
    {
        EnsureBufferLength(_settings.Count + 1);

        _valuesBuffer[0] = gesture;
        for (var i = 0; i < _settings.Count; i++)
            _valuesBuffer[i + 1] = _settings[i].Value;

        return _valuesBuffer;
    }

    private void EnsureBufferLength(int length)
    {
        if (_valuesBuffer == null || _valuesBuffer.Length != length)
            _valuesBuffer = new object[length];
    }
}
