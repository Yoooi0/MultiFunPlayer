using MultiFunPlayer.Common;
using Newtonsoft.Json;

namespace MultiFunPlayer.Settings.Converters;

internal sealed class ProtectedStringConverter : JsonConverter<string>
{
    public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var protectedValue = reader.Value as string;
        try
        {
            return ProtectedStringUtils.Unprotect(protectedValue);
        }
        catch (Exception e)
        {
            var message = $"Failed to decrypt value. Path '{reader.Path}', value '{protectedValue}'";
            if (reader is IJsonLineInfo lineInfo && lineInfo.HasLineInfo())
                message += $", line {lineInfo.LineNumber}, position {lineInfo.LinePosition}";
            message += ".";

            throw new JsonReaderException(message, e);
        }
    }

    public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
    {
        try
        {
            writer.WriteValue(ProtectedStringUtils.Protect(value));
        }
        catch (Exception e)
        {
            throw new JsonWriterException($"Failed to encrypt value. Path '{writer.Path}', value '{value}'.", e);
        }
    }
}