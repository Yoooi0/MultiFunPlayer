using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

internal abstract class NullableConverter<T>(T nullValue, T notNullValue) : IValueConverter
{
    public T NullValue { get; } = nullValue;
    public T NotNullValue { get; } = notNullValue;

    public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is string s && string.IsNullOrWhiteSpace(s)) || value is null ? NullValue : NotNullValue;

    public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class NullableToBooleanConverter : NullableConverter<bool>
{
    public NullableToBooleanConverter()
        : base(true, false) { }
}

internal sealed class InvertedNullableToBooleanConverter : NullableConverter<bool>
{
    public InvertedNullableToBooleanConverter()
        : base(false, true) { }
}