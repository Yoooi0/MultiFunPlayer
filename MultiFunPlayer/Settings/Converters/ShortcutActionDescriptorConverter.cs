using MultiFunPlayer.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Settings.Converters;

public class ShortcutActionDescriptorConverter : JsonConverter<IShortcutActionDescriptor>
{
    public override IShortcutActionDescriptor ReadJson(JsonReader reader, Type objectType, IShortcutActionDescriptor existingValue, bool hasExistingValue, JsonSerializer serializer)
        => reader.Value is string name ? new ShortcutActionDescriptor(name) : default;

    public override void WriteJson(JsonWriter writer, IShortcutActionDescriptor value, JsonSerializer serializer)
        => JToken.FromObject(value.Name).WriteTo(writer);
}
