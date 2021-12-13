using MultiFunPlayer.Common;

namespace MultiFunPlayer.Input;

public interface IShortcutSettingBuilder<T>
{
    public IShortcutSetting<T> Build();
    public IShortcutSettingBuilder<T> WithDefaultValue(T defaultValue);
    public IShortcutSettingBuilder<T> WithLabel(string label);
    public IShortcutSettingBuilder<T> WithDescription(string description);
    public IShortcutSettingBuilder<T> WithItemsSource(IEnumerable<T> itemsSource);
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat);
}

public class ShortcutSettingBuilder<T> : IShortcutSettingBuilder<T>
{
    private T _defaultValue;
    public string _description;
    public string _label;
    public ObservableConcurrentCollection<T> _itemsSource;
    private string _stringFormat;

    public IShortcutSetting<T> Build()
    {
        if (_itemsSource == null)
            return new ShortcutSetting<T>()
            {
                Description = _description,
                Label = _label,
                StringFormat = _stringFormat,
                Value = _defaultValue
            };

        return new OneOfShortcutSetting<T>()
        {
            Description = _description,
            Label = _label,
            ItemsSource = _itemsSource,
            StringFormat = _stringFormat,
            Value = _defaultValue
        };
    }

    public IShortcutSettingBuilder<T> WithDefaultValue(T defaultValue) { _defaultValue = defaultValue; return this; }
    public IShortcutSettingBuilder<T> WithItemsSource(IEnumerable<T> itemsSource) { _itemsSource = new ObservableConcurrentCollection<T>(itemsSource); return this; }
    public IShortcutSettingBuilder<T> WithDescription(string description) { _description = description; return this; }
    public IShortcutSettingBuilder<T> WithLabel(string label) { _label = label; return this; }
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat) { _stringFormat = stringFormat; return this; }
}
