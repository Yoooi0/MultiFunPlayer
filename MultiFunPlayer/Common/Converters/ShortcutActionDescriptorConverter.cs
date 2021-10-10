using MultiFunPlayer.Common.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common.Converters
{
    public class ShortcutActionDescriptorConverter : JsonConverter<IShortcutActionDescriptor>
    {
        public override IShortcutActionDescriptor ReadJson(JsonReader reader, Type objectType, IShortcutActionDescriptor existingValue, bool hasExistingValue, JsonSerializer serializer)
            => reader.Value is string name ? new ShortcutActionDescriptor(name) : default;

        public override void WriteJson(JsonWriter writer, IShortcutActionDescriptor value, JsonSerializer serializer)
            => JToken.FromObject(value.Name).WriteTo(writer);
    }
}
