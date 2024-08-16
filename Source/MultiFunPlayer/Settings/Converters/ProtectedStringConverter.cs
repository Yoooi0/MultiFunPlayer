using MultiFunPlayer.Common;
using Newtonsoft.Json;

namespace MultiFunPlayer.Settings.Converters;

internal sealed class ProtectedStringConverter : JsonConverter<string>
{
    public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer)
        => reader.Value is string encrypted && ProtectedStringUtils.TryUnprotect(encrypted, out var decrypted) ? decrypted : null;

    public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
        => writer.WriteValue(ProtectedStringUtils.Protect(value, _ => { }));
}
