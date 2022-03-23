﻿using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MultiFunPlayer.Common;

public enum ScriptResourceOrigin
{
    Automatic,
    User,
    Link,
    External
}

public enum ScriptDataType
{
    Funscript
}

public interface IScriptResource
{
    string Name { get; }
    string Source { get; }
    ScriptResourceOrigin Origin { get; }
    KeyframeCollection Keyframes { get; }
}

public class ScriptResource : IScriptResource
{
    public string Name { get; }
    public string Source { get; }
    public ScriptResourceOrigin Origin { get; }
    public KeyframeCollection Keyframes { get; }

    protected ScriptResource(string name, string source, ReadOnlySpan<byte> data, ScriptDataType type, ScriptResourceOrigin origin)
    {
        Name = name;
        Source = source;
        Origin = origin;
        Keyframes = type.Parse(data);
    }

    public static IScriptResource FromBytes(string name, string source, ReadOnlySpan<byte> bytes, ScriptResourceOrigin origin)
        => new ScriptResource(name, source, bytes, ScriptDataType.Funscript, origin);

    public static IScriptResource FromPath(string path, bool userLoaded = false) => FromFileInfo(new FileInfo(path), userLoaded);
    public static IScriptResource FromFileInfo(FileInfo file, bool userLoaded = false)
    {
        if (!file.Exists)
            return null;

        var path = file.FullName;
        var origin = userLoaded ? ScriptResourceOrigin.User : ScriptResourceOrigin.Automatic;
        return FromBytes(Path.GetFileName(path), Path.GetDirectoryName(path), File.ReadAllBytes(path), origin);
    }

    public static IScriptResource FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry, bool userLoaded = false)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        var origin = userLoaded ? ScriptResourceOrigin.User : ScriptResourceOrigin.Automatic;
        return FromBytes(entry.Name, archivePath, memory.ToArray(), origin);
    }

    public static LinkedScriptResource LinkTo(IScriptResource other) => other != null ? new LinkedScriptResource(other) : null;
}

public class LinkedScriptResource : IScriptResource
{
    private readonly IScriptResource _linked;

    public string Name => _linked.Name;
    public string Source => _linked.Source;
    public ScriptResourceOrigin Origin => ScriptResourceOrigin.Link;
    public KeyframeCollection Keyframes => _linked.Keyframes;

    public LinkedScriptResource(IScriptResource linked)
    {
        _linked = linked;
    }
}

public static class ScriptDataTypeExtensions
{
    public static KeyframeCollection Parse(this ScriptDataType type, ReadOnlySpan<byte> data)
    {
        if (data == null)
            return null;

        try
        {
            return type switch
            {
                ScriptDataType.Funscript => ParseFunscript(Encoding.UTF8.GetString(data)),
                _ => throw new NotSupportedException(),
            };
        }
        catch { }

        return null;
    }

    private static KeyframeCollection ParseFunscript(string data)
    {
        static JArray GetArray(JObject document, string propertyName)
        {
            if (!document.TryGetValue(propertyName, out var property) || property is not JArray array || array.Count == 0)
                return null;
            return array;
        }

        var document = JObject.Parse(data);

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