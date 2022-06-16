using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Text;

namespace MultiFunPlayer.Common;

public enum ScriptType
{
    Funscript,
    Csv
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
        [ScriptType.Funscript] = FunscriptReader.Default,
        [ScriptType.Csv] = CsvReader.Default
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

        foreach (var action in actions)
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

public class CsvReader : IScriptReader
{
    public static readonly CsvReader Default = new();

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);

        var keyframes = new KeyframeCollection()
        {
            IsRawCollection = true
        };

        var line = default(string);
        while ((line = streamReader.ReadLine()) != null)
        {
            var items = line.Split(';');
            if (items.Length != 2)
                continue;

            if (!float.TryParse(items[0].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position)
             || !float.TryParse(items[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var value))
                continue;

            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }
}