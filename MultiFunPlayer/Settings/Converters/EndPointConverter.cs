using Buttplug;
using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace MultiFunPlayer.Settings.Converters;

public class EndPointConverter : JsonConverter<EndPoint>
{
    public override EndPoint ReadJson(JsonReader reader, Type objectType, EndPoint existingValue, bool hasExistingValue, JsonSerializer serializer)
        => NetUtils.ParseEndpoint(reader.Value as string);

    public override void WriteJson(JsonWriter writer, EndPoint value, JsonSerializer serializer)
        => JToken.FromObject(value.ToUriString()).WriteTo(writer);
}
