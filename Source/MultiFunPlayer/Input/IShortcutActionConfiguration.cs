using Stylet;
using System.ComponentModel;

namespace MultiFunPlayer.Input;

public interface IShortcutActionConfiguration
{
    IShortcutActionDescriptor Descriptor { get; }
    IReadOnlyList<IShortcutSetting> Settings { get; }

    object[] GetActionParams();
    object[] GetActionParamsWithGesture(IInputGesture gesture);
}

public class ShortcutActionConfiguration : PropertyChangedBase, IShortcutActionConfiguration
{
    private readonly List<IShortcutSetting> _settings;
    private object[] _valuesBuffer;

    public IShortcutActionDescriptor Descriptor { get; }
    public IReadOnlyList<IShortcutSetting> Settings => _settings;

    public ShortcutActionConfiguration(IShortcutActionDescriptor descriptor, IEnumerable<IShortcutSetting> settings)
    {
        Descriptor = descriptor;

        _settings = settings.ToList();
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
