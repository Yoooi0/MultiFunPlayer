using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MultiFunPlayer.Settings.Converters;

public class TypedValueConverter : JsonConverter<TypedValue>
{
    public override TypedValue ReadJson(JsonReader reader, Type objectType, TypedValue existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;
        var valueType = o.GetTypeProperty();

        if (o.ContainsKey("Value"))
            return new TypedValue(valueType, o["Value"].ToObject(valueType));

        o.Remove("$type");
        return new TypedValue(valueType, o.ToObject(valueType));
    }

    public override void WriteJson(JsonWriter writer, TypedValue value, JsonSerializer serializer)
    {
        var valueToken = value.Value == null ? JValue.CreateNull() : JToken.FromObject(value.Value, serializer);
        var o = new JObject();
        o.AddTypeProperty(value.Type);

        if (valueToken.Type == JTokenType.Object)
            o.Merge(valueToken, new JsonMergeSettings() { MergeArrayHandling = MergeArrayHandling.Union });
        else
            o["Value"] = valueToken;

        serializer.Serialize(writer, o);
    }
}
