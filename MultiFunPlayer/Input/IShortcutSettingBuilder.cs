using Stylet;

namespace MultiFunPlayer.Input;

public interface IShortcutSettingBuilder { }

public interface IShortcutSettingBuilder<T> : IShortcutSettingBuilder
{
    public IShortcutSetting<T> Build();
    public IShortcutSettingBuilder<T> WithLabel(string label);
    public IShortcutSettingBuilder<T> WithDescription(string description);
    public IShortcutSettingBuilder<T> WithItemsSource(IEnumerable<T> items);
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat);
}

public class ShortcutSettingBuilder<T> : IShortcutSettingBuilder<T>
{
    public string _description;
    public string _label;
    public BindableCollection<T> _items;
    private string _stringFormat;

    public IShortcutSetting<T> Build()
    {
        if (_items == null)
            return new ShortcutSetting<T>()
            {
                Description = _description,
                Label = _label,
                StringFormat = _stringFormat
            };

        return new OneOfShortcutSetting<T>()
        {
            Description = _description,
            Label = _label,
            ItemsSource = _items,
            StringFormat = _stringFormat
        };
    }

    public IShortcutSettingBuilder<T> WithItemsSource(IEnumerable<T> items) { _items = new BindableCollection<T>(items); return this; }
    public IShortcutSettingBuilder<T> WithDescription(string description) { _description = description; return this; }
    public IShortcutSettingBuilder<T> WithLabel(string label) { _label = label; return this; }
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat) { _stringFormat = stringFormat; return this; }
}
