﻿namespace MultiFunPlayer.Shortcut;

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
    IShortcutSettingBuilder<T> WithStringFormat(string stringFormat);
    IShortcutSettingBuilder<T> WithTemplateName(string templateName);
    IShortcutSettingBuilder<T> WithCustomToString(Func<T, string> toString);
}

internal sealed class ShortcutSettingBuilder<T> : IShortcutSettingBuilder<T>
{
    private T _defaultValue;
    private string _description;
    private string _label;
    private IEnumerable<T> _itemsSource;
    private string _stringFormat;
    private string _templateName;
    private Func<T, string> _toString;

    IShortcutSetting IShortcutSettingBuilder.Build() => Build();
    public IShortcutSetting<T> Build()
    {
        if (_itemsSource == null)
            return new ShortcutSetting<T>()
            {
                Description = _description,
                Label = _label,
                StringFormat = _stringFormat,
                TemplateName = _templateName,
                Value = _defaultValue,
                CustomToString = _toString
            };

        return new OneOfShortcutSetting<T>()
        {
            Description = _description,
            Label = _label,
            ItemsSource = _itemsSource,
            StringFormat = _stringFormat,
            TemplateName = _templateName,
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
    public IShortcutSettingBuilder<T> WithStringFormat(string stringFormat) { _stringFormat = stringFormat; return this; }
    public IShortcutSettingBuilder<T> WithTemplateName(string templateName) { _templateName = templateName; return this; }
    public IShortcutSettingBuilder<T> WithCustomToString(Func<T, string> toString) { _toString = toString; return this; }
}
