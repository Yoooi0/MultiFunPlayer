using Newtonsoft.Json;
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
    KeyframeCollection Read(Stream stream);

    KeyframeCollection Read(IEnumerable<byte> bytes) => Read(bytes.ToArray());
    KeyframeCollection Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return Read(stream);
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

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        using var jsonReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault();

        var script = serializer.Deserialize<Script>(jsonReader);
        if (script.RawActions == null && script.Actions == null)
            return null;

        var isRaw = script.RawActions?.Count > script.Actions?.Count;
        var actions = isRaw ? script.RawActions : script.Actions;
        var keyframes = new KeyframeCollection(actions.Count)
        {
            IsRawCollection = isRaw
        };

        foreach(var action in actions)
        {
            var position = action.At / 1000;
            if (position < 0)
                continue;

            var value = action.Pos / 100;
            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }

    private record Script(List<Action> RawActions, List<Action> Actions);
    private record Action(float At, float Pos);
}