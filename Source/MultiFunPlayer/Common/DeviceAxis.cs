using MultiFunPlayer.Settings.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace MultiFunPlayer.Common;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
[TypeConverter(typeof(DeviceAxisTypeConverter))]
public sealed class DeviceAxis
{
    private int _id;

    [JsonProperty] public string Name { get; init; }
    [JsonProperty] public double DefaultValue { get; init; }
    [JsonProperty] public string FriendlyName { get; init; }
    [JsonProperty] public IReadOnlyList<string> FunscriptNames { get; init; }
    [JsonProperty] public bool LoadUnnamedScript { get; init; }

    public override string ToString() => Name;
    public override int GetHashCode() => _id;

    [OnDeserialized]
    internal void OnDeserializedMethod(StreamingContext context) => _id = _count++;

    public static implicit operator DeviceAxis(string name) => TryParse(name, out var axis) ? axis : null;

    public static bool operator !=(DeviceAxis left, DeviceAxis right) => !ReferenceEquals(left, right);
    public static bool operator ==(DeviceAxis left, DeviceAxis right) => ReferenceEquals(left, right);
    public override bool Equals(object obj) => obj != null && ReferenceEquals(this, obj);

    private static int _count;
    private static int _outputMaximum;
    private static string _outputFormat;
    private static Dictionary<string, DeviceAxis> _axes;
    public static IReadOnlyCollection<DeviceAxis> All => _axes.Values;

    public static DeviceAxis Parse(string name) => _axes.GetValueOrDefault(name, null);
    public static IEnumerable<DeviceAxis> Parse(params string[] names) => names.Select(n => Parse(n));

    public static bool TryParse(string name, out DeviceAxis axis)
    {
        axis = Parse(name);
        return axis != null;
    }

    public static string ToString(DeviceAxis axis, double value) => $"{axis}{string.Format(_outputFormat, value * _outputMaximum)}";
    public static string ToString(DeviceAxis axis, double value, double interval) => $"{ToString(axis, value)}I{(int)Math.Floor(interval + 0.75)}";

    public static string ToString(IEnumerable<KeyValuePair<DeviceAxis, double>> values)
        => $"{values.Aggregate(string.Empty, (s, x) => $"{s} {ToString(x.Key, x.Value)}")}\n".TrimStart();
    public static string ToString(IEnumerable<KeyValuePair<DeviceAxis, double>> values, double interval)
        => $"{values.Aggregate(string.Empty, (s, x) => $"{s} {ToString(x.Key, x.Value, interval)}")}\n".TrimStart();

    public static bool IsValueDirty(double value, double lastValue)
        => Math.Abs(lastValue - value) * (_outputMaximum + 1) >= 1 || (double.IsFinite(value) ^ double.IsFinite(lastValue));
    public static bool IsValueDirty(double value, double lastValue, double epsilon)
        => Math.Abs(lastValue - value) >= epsilon || (double.IsFinite(value) ^ double.IsFinite(lastValue));

    internal static void LoadSettings(JObject settings, JsonSerializer serializer)
    {
        var enabledAxes = JArray.FromObject((settings["Axes"] as JArray).Where(x => x["Enabled"].ToObject<bool>()));
        if (!enabledAxes.TryToObject<List<DeviceAxis>>(serializer, out var axes)
         || !settings.TryGetValue<int>("OutputPrecision", serializer, out var precision))
            throw new JsonReaderException("Unable to read device settings");

        _outputMaximum = (int)(Math.Pow(10, precision) - 1);
        _outputFormat = $"{{0:{new string('0', precision)}}}";
        _axes = axes.ToDictionary(a => a.Name, a => a);
    }
}
