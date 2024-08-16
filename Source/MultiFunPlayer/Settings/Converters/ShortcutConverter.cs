using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Converters;

[GlobalJsonConverter]
internal sealed class ShortcutConverter(IShortcutFactory shortcutFactory) : JsonConverter<IShortcut>
{
    public override bool CanWrite => false;

    public override IShortcut ReadJson(JsonReader reader, Type objectType, IShortcut existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var o = JToken.ReadFrom(reader) as JObject;

        var gesture = o[nameof(IShortcut.Gesture)].ToObject<TypedValue>();
        var instance = shortcutFactory.CreateShortcut(o.GetTypeProperty(), (IInputGestureDescriptor)gesture.Value);

        o.Remove(nameof(IShortcut.Gesture));
        o.Populate(instance);
        return instance;
    }

    public override void WriteJson(JsonWriter writer, IShortcut value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
