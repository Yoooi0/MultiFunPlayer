using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace MultiFunPlayer.Settings.Converters;

public class IPEndPointConverter : JsonConverter<IPEndPoint>
{
    public override IPEndPoint ReadJson(JsonReader reader, Type objectType, IPEndPoint existingValue, bool hasExistingValue, JsonSerializer serializer)
        => reader.Value is string s && IPEndPoint.TryParse(s, out var result) ? result : null;

    public override void WriteJson(JsonWriter writer, IPEndPoint value, JsonSerializer serializer)
        => JToken.FromObject(value.ToString()).WriteTo(writer);
}
