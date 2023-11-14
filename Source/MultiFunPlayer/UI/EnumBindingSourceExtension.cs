using System.Windows.Markup;

namespace MultiFunPlayer.UI;

public class EnumBindingSourceExtension : MarkupExtension
{
    private Type _enumType;
    public Type EnumType
    {
        get => _enumType;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var enumType = Nullable.GetUnderlyingType(value) ?? value;
            if (!enumType.IsEnum)
                throw new ArgumentException("{enumType} is not an Enum");

            _enumType = value;
        }
    }

    public EnumBindingSourceExtension(Type enumType)
    {
        EnumType = enumType;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var actualEnumType = Nullable.GetUnderlyingType(_enumType) ?? _enumType;
        var enumValues = Enum.GetValues(actualEnumType);

        if (actualEnumType == _enumType)
            return enumValues;

        var result = Array.CreateInstance(actualEnumType, enumValues.Length + 1);
        enumValues.CopyTo(result, 1);
        return result;
    }
}
