using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MultiFunPlayer.Settings.Converters
{
    public class FileSystemInfoConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => typeof(FileSystemInfo).IsAssignableFrom(objectType);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            => reader.Value is not string s ? null : Activator.CreateInstance(objectType, s);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            => JToken.FromObject((value as FileSystemInfo)?.FullName).WriteTo(writer);
    }
}
