using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using MultiFunPlayer.OutputTarget;
using MultiFunPlayer.Common;

namespace MultiFunPlayer.Settings.Converters;

internal class OutputTargetConverter : JsonConverter<IOutputTarget>
{
    private readonly IOutputTargetFactory _outputTargetFactory;

    public OutputTargetConverter(IOutputTargetFactory outputTargetFactory) => _outputTargetFactory = outputTargetFactory;

    public override IOutputTarget ReadJson(JsonReader reader, Type objectType, IOutputTarget existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;

        var type = o.GetTypeProperty()
            ?? throw new JsonReaderException($"Failed to find output target type \"{o["$type"]}\"");

        var index = o["$index"].ToObject<int>();
        o.Remove("$type");
        o.Remove("$index");

        var instance = _outputTargetFactory.CreateOutputTarget(type, index)
            ?? throw new JsonReaderException($"Failed to create instance of \"{type}\" with \"{index}\" index");

        instance.HandleSettings(o, SettingsAction.Loading);
        return instance;
    }

    public override void WriteJson(JsonWriter writer, IOutputTarget value, JsonSerializer serializer)
    {
        var o = new JObject() { ["$index"] = value.InstanceIndex, };
        o.AddTypeProperty(value.GetType());

        value.HandleSettings(o, SettingsAction.Saving);
        serializer.Serialize(writer, o);
    }
}
