using System.IO;
using System.IO.Compression;

namespace MultiFunPlayer.Common;

public interface IScriptResource
{
    string Name { get; }
    string Source { get; }
    KeyframeCollection Keyframes { get; }
}

public class ScriptResource : IScriptResource
{
    public string Name { get; }
    public string Source { get; }
    public KeyframeCollection Keyframes { get; }

    protected ScriptResource(string name, string source, KeyframeCollection keyframes)
    {
        Name = name;
        Source = source;
        Keyframes = keyframes;
    }

    public static IScriptResource FromPath(IScriptReader reader, string path) => FromFileInfo(reader, new FileInfo(path));
    public static IScriptResource FromFileInfo(IScriptReader reader, FileInfo file)
    {
        if (!file.Exists)
            return null;

        var path = file.FullName;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var keyframes = reader.Read(stream);
        return new ScriptResource(Path.GetFileName(path), Path.GetDirectoryName(path), keyframes);
    }

    public static IScriptResource FromZipArchiveEntry(IScriptReader reader, string archivePath, ZipArchiveEntry entry)
    {
        using var stream = entry.Open();

        var keyframes = reader.Read(stream);
        return new ScriptResource(entry.Name, archivePath, keyframes);
    }

    public static IScriptResource FromBytes(IScriptReader reader, string name, string source, IEnumerable<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        var keyframes = reader.Read(stream);
        return new ScriptResource(name, source, keyframes);
    }

    public static LinkedScriptResource LinkTo(IScriptResource other) => other != null ? new LinkedScriptResource(other) : null;
}

public class LinkedScriptResource : IScriptResource
{
    private readonly IScriptResource _linked;

    public string Name => _linked.Name;
    public string Source => _linked.Source;
    public KeyframeCollection Keyframes => _linked.Keyframes;

    public LinkedScriptResource(IScriptResource linked)
    {
        _linked = linked;
    }
}