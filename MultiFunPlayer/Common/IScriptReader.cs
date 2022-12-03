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
}

public class FunscriptReaderSettings { }

public class FunscriptReader : IScriptReader
{
    public static FunscriptReader Default { get; } = new FunscriptReader();

    public FunscriptReader() : this(new FunscriptReaderSettings()) { }
    public FunscriptReader(FunscriptReaderSettings settings) { }

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        using var jsonReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault();

        var script = serializer.Deserialize<Script>(jsonReader);
        if (script.Actions == null)
            return null;

        var keyframes = new KeyframeCollection(script.Actions.Count);
        foreach (var action in script.Actions)
        {
            var position = action.At / 1000;
            if (position < 0)
                continue;

            var value = MathUtils.Clamp01(action.Pos / 100);
            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }

    private record Script(List<Action> Actions);
    private record Action(double At, double Pos);
}

public class CsvReaderSettings { }

public class CsvReader : IScriptReader
{
    public static CsvReader Default { get; } = new CsvReader();

    public CsvReader() : this(new CsvReaderSettings()) { }
    public CsvReader(CsvReaderSettings settings) { }

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);

        var keyframes = new KeyframeCollection();

        var line = default(string);
        while ((line = streamReader.ReadLine()) != null)
        {
            var items = line.Split(';');
            if (items.Length != 2)
                continue;

            if (!double.TryParse(items[0].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position)
             || !double.TryParse(items[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var value))
                continue;

            if (position < 0)
                continue;

            value = MathUtils.Clamp01(value);
            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }
}