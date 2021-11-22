using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MultiFunPlayer.Settings.Converters;

public class TypedValueConverter : JsonConverter<TypedValue>
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

    public override TypedValue ReadJson(JsonReader reader, Type objectType, TypedValue existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader);

        var (_, valueTypeName) = SplitFullyQualifiedTypeName(o["$type"].ToString());
        var valueType = Type.GetType(valueTypeName);
        var value = o["Value"].ToObject(valueType);

        return new TypedValue(valueType, value);
    }

    public override void WriteJson(JsonWriter writer, TypedValue value, JsonSerializer serializer)
    {
        var valueToken = new JObject
        {
            { "$type", RemoveAssemblyDetails(value.Type.AssemblyQualifiedName) },
            { "Value", JToken.FromObject(value.Value, serializer) }
        };

        serializer.Serialize(writer, valueToken);
    }
}
