using Newtonsoft.Json;

namespace MultiFunPlayer.Settings;

public class TypedValue
{
    [JsonProperty("$type")]
    public Type Type { get; }
    public object Value { get; }

    public TypedValue(Type type, object value)
    {
        Type = type;
        Value = value;
    }

    public void Deconstruct(out Type type, out object value)
    {
        type = Type;
        value = Value;
    }
}
