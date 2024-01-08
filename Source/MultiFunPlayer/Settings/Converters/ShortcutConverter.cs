using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Converters;

internal sealed class ShortcutConverter(IShortcutActionResolver actionResolver) : JsonConverter<IShortcut>
{
    public override IShortcut ReadJson(JsonReader reader, Type objectType, IShortcut existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;

        var gesture = o[nameof(IShortcut.Gesture)].ToObject<TypedValue>();
        var instance = (IShortcut)Activator.CreateInstance(o.GetTypeProperty(), [actionResolver, gesture.Value]);

        o.Remove(nameof(IShortcut.Gesture));
        o.Populate(instance);
        return instance;
    }

    public override void WriteJson(JsonWriter writer, IShortcut value, JsonSerializer serializer)
        => serializer.Serialize(writer, value);
}
