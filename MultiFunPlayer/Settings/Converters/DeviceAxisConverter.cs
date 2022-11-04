using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Globalization;

namespace MultiFunPlayer.Settings.Converters;

internal class DeviceAxisConverter : JsonConverter<DeviceAxis>
{
    public override DeviceAxis ReadJson(JsonReader reader, Type objectType, DeviceAxis existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.Value is not string name)
            return null;

        if (DeviceAxis.TryParse(name, out var axis))
            return axis;

        return null;
    }

    public override void WriteJson(JsonWriter writer, DeviceAxis value, JsonSerializer serializer)
    {
        if (value == null)
            return;

        JToken.FromObject(value.Name).WriteTo(writer);
    }
}

internal class DeviceAxisTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => sourceType == typeof(string);
    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => destinationType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string name && DeviceAxis.TryParse(name, out var axis))
            return axis;

        return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is DeviceAxis axis)
            return axis.Name;

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
