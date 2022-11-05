using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Text;

namespace MultiFunPlayer.Common;

internal enum ScriptType
{
    Funscript,
    Csv
}

internal interface IScriptReader<TSettings>
{
    KeyframeCollection Read(Stream stream, TSettings settings = default);
}

internal class FunscriptReaderSettings
{
    public bool PreferRawActions { get; init; } = true;
}

internal class FunscriptReader : IScriptReader<FunscriptReaderSettings>
{
    public static readonly FunscriptReader Default = new();

    public KeyframeCollection Read(Stream stream, FunscriptReaderSettings settings = null)
    {
        settings ??= new FunscriptReaderSettings();

        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        using var jsonReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault();

        var script = serializer.Deserialize<Script>(jsonReader);
        if (script.RawActions == null && script.Actions == null)
            return null;

        var isRaw = settings.PreferRawActions && script.RawActions?.Count > script.Actions?.Count;
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
    private record Action(double At, double Pos);
}

internal class CsvReaderSettings
{
    public bool CreateRawCollection { get; init; } = true;
}

internal class CsvReader : IScriptReader<CsvReaderSettings>
{
    public static readonly CsvReader Default = new();

    public KeyframeCollection Read(Stream stream, CsvReaderSettings settings = null)
    {
        settings ??= new CsvReaderSettings();

        using var streamReader = new StreamReader(stream, Encoding.UTF8);

        var keyframes = new KeyframeCollection()
        {
            IsRawCollection = settings.CreateRawCollection
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

            keyframes.Add(new Keyframe(position, value));
        }

        return keyframes;
    }
}