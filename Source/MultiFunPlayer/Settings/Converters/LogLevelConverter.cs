using Newtonsoft.Json;
using NLog;

namespace MultiFunPlayer.Settings.Converters;

internal sealed class LogLevelConverter : JsonConverter<LogLevel>
{
    public override LogLevel ReadJson(JsonReader reader, Type objectType, LogLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
        => LogLevel.FromString(reader.Value as string);

    public override void WriteJson(JsonWriter writer, LogLevel value, JsonSerializer serializer)
        => writer.WriteValue(value.Name);
}
