using MultiFunPlayer.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MultiFunPlayer.Settings.Converters;

public class ShortcutActionConverter : JsonConverter<IShortcutAction>
{
    private string RemoveAssemblyDetails(string fullyQualifiedTypeName)
    {
        var builder = new StringBuilder();

        var writingAssemblyName = false;
        var skippingAssemblyDetails = false;
        var followBrackets = false;
        for (var i = 0; i < fullyQualifiedTypeName.Length; i++)
        {
            var current = fullyQualifiedTypeName[i];
            switch (current)
            {
                case '[':
                    writingAssemblyName = false;
                    skippingAssemblyDetails = false;
                    followBrackets = true;
                    builder.Append(current);
                    break;
                case ']':
                    writingAssemblyName = false;
                    skippingAssemblyDetails = false;
                    followBrackets = false;
                    builder.Append(current);
                    break;
                case ',':
                    if (followBrackets)
                    {
                        builder.Append(current);
                    }
                    else if (!writingAssemblyName)
                    {
                        writingAssemblyName = true;
                        builder.Append(current);
                    }
                    else
                    {
                        skippingAssemblyDetails = true;
                    }

                    break;
                default:
                    followBrackets = false;
                    if (!skippingAssemblyDetails)
                        builder.Append(current);
                    break;
            }
        }

        return builder.ToString();
    }

    public static (string AssemblyName, string TypeName) SplitFullyQualifiedTypeName(string fullyQualifiedTypeName)
    {
        static int? GetAssemblyDelimiterIndex(string fullyQualifiedTypeName)
        {
            var scope = 0;
            for (var i = 0; i < fullyQualifiedTypeName.Length; i++)
            {
                var current = fullyQualifiedTypeName[i];
                switch (current)
                {
                    case '[':
                        scope++;
                        break;
                    case ']':
                        scope--;
                        break;
                    case ',':
                        if (scope == 0)
                            return i;
                        break;
                }
            }

            return null;
        }

        static string Trim(string s, int start, int length)
        {
            var end = start + length - 1;
            for (; start < end; start++)
                if (!char.IsWhiteSpace(s[start]))
                    break;

            for (; end >= start; end--)
                if (!char.IsWhiteSpace(s[end]))
                    break;

            return s.Substring(start, end - start + 1);
        }

        var assemblyDelimiterIndex = GetAssemblyDelimiterIndex(fullyQualifiedTypeName);
        if (assemblyDelimiterIndex != null)
        {
            return (Trim(fullyQualifiedTypeName, (int)assemblyDelimiterIndex + 1, fullyQualifiedTypeName.Length - (int)assemblyDelimiterIndex - 1), 
                    Trim(fullyQualifiedTypeName, 0, (int)assemblyDelimiterIndex));
        }

        return (null, fullyQualifiedTypeName);
    }

    public override IShortcutAction ReadJson(JsonReader reader, Type objectType, IShortcutAction existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JObject.Load(reader);
        var (_, typeName) = SplitFullyQualifiedTypeName(o["$type"].ToString());
        var specifiedType = Type.GetType(typeName);

        var descriptor = o["Descriptor"].ToObject<IShortcutActionDescriptor>(); 
        var args = new List<object>
        {
            descriptor,
            null
        };

        if (o.ContainsKey("Settings"))
        {
            var values = o["Settings"].ToArray();

            var genericTypes = specifiedType.GetGenericArguments();
            var settings = new List<IShortcutSetting>();
            for (var i = 0; i < values.Length; i++)
            {
                var valueType = genericTypes[i];
                var settingType = typeof(ShortcutSetting<>).MakeGenericType(valueType);
                var setting = (IShortcutSetting)Activator.CreateInstance(settingType);

                setting.Value = values[i].ToObject(valueType);
                settings.Add(setting);
            }

            args.AddRange(settings);
        }

        return (IShortcutAction)Activator.CreateInstance(specifiedType, args.ToArray());
    }

    public override void WriteJson(JsonWriter writer, IShortcutAction value, JsonSerializer serializer)
    {
        writer.WriteStartObject();

        var typeName = RemoveAssemblyDetails(value.GetType().AssemblyQualifiedName);
        writer.WritePropertyName("$type", false);
        writer.WriteValue(typeName);

        writer.WritePropertyName("Descriptor", false);
        serializer.Serialize(writer, value.Descriptor);

        if (value.Settings.Any())
        {
            writer.WritePropertyName("Settings", false);
            serializer.Serialize(writer, value.Settings.Select(s => s.Value));
        }

        writer.WriteEndObject();
    }
}
