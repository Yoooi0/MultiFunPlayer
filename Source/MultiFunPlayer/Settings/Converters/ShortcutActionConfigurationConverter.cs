using MultiFunPlayer.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Converters;

internal class ShortcutActionConfigurationConverter : JsonConverter<IShortcutActionConfiguration>
{
    private readonly IShortcutManager _manager;

    public ShortcutActionConfigurationConverter(IShortcutManager manager) => _manager = manager;

    public override IShortcutActionConfiguration ReadJson(JsonReader reader, Type objectType, IShortcutActionConfiguration existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;
        var actionName = o[nameof(IShortcutActionConfiguration.Name)].ToString();

        var configuration = _manager.CreateShortcutActionConfigurationInstance(actionName)
            ?? throw new JsonReaderException($"Unable to find \"{actionName}\" shortcut action");

        var settings = o[nameof(IShortcutActionConfiguration.Settings)].ToObject<List<TypedValue>>();
        configuration.Populate(settings);
        return configuration;
    }

    public override void WriteJson(JsonWriter writer, IShortcutActionConfiguration value, JsonSerializer serializer)
    {
        var o = new JObject
        {
            [nameof(IShortcutActionConfiguration.Name)] = value.Name,
            [nameof(IShortcutActionConfiguration.Settings)] = JArray.FromObject(value.Settings.Select(s => new TypedValue(s.Type, s.Value)))
        };

        o.WriteTo(writer);
    }
}
