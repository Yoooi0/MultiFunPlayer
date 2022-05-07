using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;

namespace MultiFunPlayer.Common;

public enum ScriptType
{
    Funscript
}

public interface IScriptReader
{
    KeyframeCollection Read(byte[] bytes);

    KeyframeCollection Read(IEnumerable<byte> bytes) => Read(bytes.ToArray());
    KeyframeCollection Read(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }
}

public static class ScriptReader
{
    private static readonly Dictionary<ScriptType, IScriptReader> _scriptReaders = new()
    {
        [ScriptType.Funscript] = FunscriptReader.Default
    };

    public static KeyframeCollection Read(ScriptType scriptType, byte[] bytes) => _scriptReaders[scriptType].Read(bytes);
    public static KeyframeCollection Read(ScriptType scriptType, IEnumerable<byte> bytes) => _scriptReaders[scriptType].Read(bytes);
    public static KeyframeCollection Read(ScriptType scriptType, Stream stream) => _scriptReaders[scriptType].Read(stream);
}

public class FunscriptReader : IScriptReader
{
    public static readonly FunscriptReader Default = new();

    public KeyframeCollection Read(byte[] bytes)
    {
        static JArray GetArray(JObject document, string propertyName)
        {
            if (!document.TryGetValue(propertyName, out var property) || property is not JArray array || array.Count == 0)
                return null;
            return array;
        }

        var document = JObject.Parse(Encoding.UTF8.GetString(bytes));

        var rawActions = GetArray(document, "rawActions");
        var actions = GetArray(document, "actions");
        if (rawActions == null && actions == null)
            return null;

        var isRaw = rawActions?.Count > actions?.Count;
        var keyframes = new KeyframeCollection()
        {
            IsRawCollection = isRaw
        };

        foreach (var child in isRaw ? rawActions : actions)
        {
            var position = child["at"].ToObject<long>() / 1000.0f;
            if (position < 0)
                continue;

            var value = child["pos"].ToObject<float>() / 100;
            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }
}