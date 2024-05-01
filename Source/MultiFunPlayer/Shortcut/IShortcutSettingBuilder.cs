using MahApps.Metro.Controls;

namespace MultiFunPlayer.Shortcut;

public interface IShortcutSettingBuilder
{
    internal IShortcutSetting Build();
}

public interface IShortcutSettingBuilder<T> : IShortcutSettingBuilder
{
    internal new IShortcutSetting<T> Build();
    IShortcutSettingBuilder<T> WithDefaultValue(T defaultValue);
    IShortcutSettingBuilder<T> WithLabel(string label);
    IShortcutSettingBuilder<T> WithDescription(string description);
    IShortcutSettingBuilder<T> WithItemsSource<TItemsSource>(TItemsSource itemsSource, bool bindsDirectlyToItemsSource = false) where TItemsSource : IEnumerable<T>;
    IShortcutSettingBuilder<T> WithTemplateName(string templateName);
    IShortcutSettingBuilder<T> WithCustomToString(Func<T, string> toString);
    IShortcutSettingBuilder<T> AsNumericUpDown(double minimum = double.MinValue, double maximum = double.MaxValue, double interval = 1d, string stringFormat = null);
}

internal sealed class ShortcutSettingBuilder<T> : IShortcutSettingBuilder<T>
{
    private T _defaultValue;
    private string _description;
    private string _label;
    private IEnumerable<T> _itemsSource;
    private string _templateName;
    private IShortcutSettingTemplateContext _templateContext;
    private Func<T, string> _toString;

    IShortcutSetting IShortcutSettingBuilder.Build() => Build();
    public IShortcutSetting<T> Build()
    {
        if (_templateContext == null)
        {
            if (typeof(T) == typeof(int) || typeof(T) == typeof(double))
                AsNumericUpDown();
        }

        if (_itemsSource == null)
            return new ShortcutSetting<T>()
            {
                Description = _description,
                Label = _label,
                TemplateName = _templateName,
                TemplateContext = _templateContext,
                Value = _defaultValue,
                CustomToString = _toString
            };

        return new OneOfShortcutSetting<T>()
        {
            Description = _description,
            Label = _label,
            ItemsSource = _itemsSource,
            TemplateName = _templateName,
            TemplateContext = _templateContext,
            Value = _defaultValue,
            CustomToString = _toString
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
    public IShortcutSettingBuilder<T> WithTemplateName(string templateName) { _templateName = templateName; return this; }
    public IShortcutSettingBuilder<T> WithCustomToString(Func<T, string> toString) { _toString = toString; return this; }

    public IShortcutSettingBuilder<T> AsNumericUpDown(double minimum = double.MinValue, double maximum = double.MaxValue, double interval = 1d, string stringFormat = null)
    {
        var numericInput = NumericInput.All;
        if (typeof(T) == typeof(int))
        {
            stringFormat ??= "{0:F0}";
            numericInput = NumericInput.Numbers;
        }

        _templateContext = new NumericUpDownShortcutSettingTemplateContext(minimum, maximum, interval, stringFormat, numericInput);
        _defaultValue = (T)Convert.ChangeType(_templateContext.CoerceValue(_defaultValue), typeof(T));
        return this;
    }
}
