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
        var descriptor = new ShortcutActionDescriptor(o[nameof(IShortcutActionConfiguration.Descriptor)].ToString());

        var configuration = _manager.CreateShortcutActionConfigurationInstance(descriptor)
            ?? throw new JsonReaderException($"Unable to find \"{descriptor}\" shortcut action");

        var settings = o[nameof(IShortcutActionConfiguration.Settings)].ToObject<List<TypedValue>>();
        configuration.Populate(settings);
        return configuration;
    }

    public override void WriteJson(JsonWriter writer, IShortcutActionConfiguration value, JsonSerializer serializer)
    {
        var o = new JObject
        {
            [nameof(IShortcutActionConfiguration.Descriptor)] = value.Descriptor.Name,
            [nameof(IShortcutActionConfiguration.Settings)] = JArray.FromObject(value.Settings.Select(s => new TypedValue(s.GetType().GetGenericArguments()[0], s.Value)))
        };

        o.WriteTo(writer);
    }
}
