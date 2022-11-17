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

public class FunscriptReaderSettings
{
    public bool PreferRawActions { get; init; } = true;
}

public class FunscriptReader : IScriptReader
{
    private readonly FunscriptReaderSettings _settings;

    public static FunscriptReader Default { get; } = new FunscriptReader();

    public FunscriptReader() : this(new FunscriptReaderSettings()) { }
    public FunscriptReader(FunscriptReaderSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        using var jsonReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault();

        var script = serializer.Deserialize<Script>(jsonReader);
        if (script.RawActions == null && script.Actions == null)
            return null;

        var isRaw = _settings.PreferRawActions && script.RawActions?.Count > script.Actions?.Count;
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

            var value = MathUtils.Clamp01(action.Pos / 100);
            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }

    private record Script(List<Action> RawActions, List<Action> Actions);
    private record Action(double At, double Pos);
}

public class CsvReaderSettings
{
    public bool CreateRawCollection { get; init; } = true;
}

public class CsvReader : IScriptReader
{
    private readonly CsvReaderSettings _settings;

    public static CsvReader Default { get; } = new CsvReader();

    public CsvReader() : this(new CsvReaderSettings()) { }
    public CsvReader(CsvReaderSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public KeyframeCollection Read(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);

        var keyframes = new KeyframeCollection()
        {
            IsRawCollection = _settings.CreateRawCollection
        };

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