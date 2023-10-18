using MultiFunPlayer.Settings;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;

namespace MultiFunPlayer.Input;

public interface IShortcutActionConfiguration
{
    string Name { get; }
    IReadOnlyList<IShortcutSetting> Settings { get; }

    void Populate(IEnumerable<object> values);
    void Populate(IEnumerable<TypedValue> values);

    object[] GetActionParams();
    object[] GetActionParamsWithGesture(IInputGesture gesture);
}

public class ShortcutActionConfiguration : PropertyChangedBase, IShortcutActionConfiguration
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly List<IShortcutSetting> _settings;
    private object[] _valuesBuffer;

    public string Name { get; }
    public IReadOnlyList<IShortcutSetting> Settings => _settings;

    public ShortcutActionConfiguration(string actionName, IEnumerable<IShortcutSetting> settings)
    {
        Name = actionName;

        _settings = settings.ToList();
        foreach (var setting in _settings)
        {
            if (setting is INotifyPropertyChanged settingPropertyChanged)
                settingPropertyChanged.PropertyChanged += OnSettingPropertyChanged;
            if (setting.Value is INotifyPropertyChanged valuePropertyChanged)
                valuePropertyChanged.PropertyChanged += OnSettingPropertyChanged;
        }
    }

    public string DisplayName => _settings.Count == 0 ? Name : $"{Name} [{string.Join(", ", Settings.Select(s => s.ToString()))}]";

    public void Populate(IEnumerable<object> values)
    {
        foreach (var (setting, value) in Settings.Zip(values))
            Populate(setting, value, value.GetType());
    }

    public void Populate(IEnumerable<TypedValue> values)
    {
        foreach (var (setting, value) in Settings.Zip(values))
            Populate(setting, value.Value, value.Type);
    }

    private void Populate(IShortcutSetting setting, object value, Type valueType)
    {
        var settingType = setting.GetType().GetGenericArguments()[0];
        var typeMatches = value == null ? !settingType.IsValueType || Nullable.GetUnderlyingType(settingType) != null
                                        : valueType == settingType || valueType.IsAssignableTo(settingType);

        if (!typeMatches)
        {
            Logger.Warn($"Action \"{Name}\" setting type mismatch! [\"{settingType}\" != \"{valueType}\"]");
        }
        else
        {
            if (setting.Value is INotifyPropertyChanged oldPropertyChanged)
                oldPropertyChanged.PropertyChanged -= OnSettingPropertyChanged;

            setting.Value = value;
            if (setting.Value is INotifyPropertyChanged newPropertyChanged)
                newPropertyChanged.PropertyChanged += OnSettingPropertyChanged;
        }
    }

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

    [SuppressPropertyChangedWarnings]
    private void OnSettingPropertyChanged(object sender, PropertyChangedEventArgs e) => NotifyOfPropertyChange(() => DisplayName);
}
