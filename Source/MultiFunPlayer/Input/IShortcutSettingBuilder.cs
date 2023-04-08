using MultiFunPlayer.Common;

namespace MultiFunPlayer.Input;

public interface IShortcutSettingBuilder
{
    public IShortcutSetting Build();
}

public interface IShortcutSettingBuilder<T> : IShortcutSettingBuilder
{
    public new IShortcutSetting<T> Build();
    public IShortcutSettingBuilder<T> WithDefaultValue(T defaultValue);
    public IShortcutSettingBuilder<T> WithLabel(string label);
    public IShortcutSettingBuilder<T> WithDescription(string description);
    public IShortcutSettingBuilder<T> WithItemsSource<TItemsSource>(TItemsSource itemsSource, bool bindsDirectlyToItemsSource = false) where TItemsSource : IEnumerable<T>;
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat);
}

public class ShortcutSettingBuilder<T> : IShortcutSettingBuilder<T>
{
    private T _defaultValue;
    private string _description;
    private string _label;
    private IEnumerable<T> _itemsSource;
    private string _stringFormat;

    IShortcutSetting IShortcutSettingBuilder.Build() => Build();
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
    public IShortcutSettingBuilder<T> WithItemsSource<TItemsSource>(TItemsSource itemsSource, bool bindsDirectlyToItemsSource = false) where TItemsSource : IEnumerable<T>
    {
        _itemsSource = bindsDirectlyToItemsSource ? itemsSource : itemsSource.ToList();
        return this;
    }

    public IShortcutSettingBuilder<T> WithDescription(string description) { _description = description; return this; }
    public IShortcutSettingBuilder<T> WithLabel(string label) { _label = label; return this; }
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat) { _stringFormat = stringFormat; return this; }
}
