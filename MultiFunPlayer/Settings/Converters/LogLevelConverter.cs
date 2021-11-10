using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Converters;

public class LogLevelConverter : JsonConverter<LogLevel>
{
    public override LogLevel ReadJson(JsonReader reader, Type objectType, LogLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
        => LogLevel.FromString(reader.Value as string);

    public override void WriteJson(JsonWriter writer, LogLevel value, JsonSerializer serializer)
        => JToken.FromObject(value.Name).WriteTo(writer);
}
