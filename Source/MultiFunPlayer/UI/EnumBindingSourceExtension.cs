using Newtonsoft.Json.Linq;
using System.Windows.Markup;

namespace MultiFunPlayer.UI;

public sealed class EnumBindingSourceExtension : MarkupExtension
{
    private readonly Type _enumType;

    public EnumBindingSourceExtension(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);

        var actualEnumType = Nullable.GetUnderlyingType(enumType) ?? enumType;
        if (!actualEnumType.IsEnum)
            throw new ArgumentException($"{enumType} is not an Enum type");

        _enumType = enumType;
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
