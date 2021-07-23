using MultiFunPlayer.Common.Converters;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace MultiFunPlayer.Common
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    [TypeConverter(typeof(DeviceAxisTypeConverter))]
    public sealed class DeviceAxis
    {
        private int _id;

        [JsonProperty] public string Name { get; init; }
        [JsonProperty] public float DefaultValue { get; init; }
        [JsonProperty] public string FriendlyName { get; init; }
        [JsonProperty] public IEnumerable<string> FunscriptNames { get; init; }

        public override string ToString() => Name;
        public override int GetHashCode() => _id;

        private static readonly Dictionary<string, DeviceAxis> _axes = new();
        public static IReadOnlyCollection<DeviceAxis> All => _axes.Values;

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            _id = _axes.Count;
            _axes[Name] = this;
        }

        public static bool TryParse(string name, out DeviceAxis axis)
        {
            axis = _axes.GetValueOrDefault(name, null);
            return axis != null;
        }
    }
}
