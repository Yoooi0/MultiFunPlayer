using Newtonsoft.Json;

namespace MultiFunPlayer.Settings;

public class TypedValue(Type type, object value)
{
    [JsonProperty("$type")]
    public Type Type { get; } = type;
    public object Value { get; } = value;

    public TypedValue(object value) : this(value.GetType(), value) { }

    public void Deconstruct(out Type type, out object value)
    {
        type = Type;
        value = Value;
    }
}
