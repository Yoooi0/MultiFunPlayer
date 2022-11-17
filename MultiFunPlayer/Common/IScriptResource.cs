using System.IO;
using System.IO.Compression;

namespace MultiFunPlayer.Common;

public enum ScriptResourceOrigin
{
    Automatic,
    User,
    Link,
    External
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

    protected ScriptResource(string name, string source, KeyframeCollection keyframes, ScriptResourceOrigin origin)
    {
        Name = name;
        Source = source;
        Origin = origin;
        Keyframes = keyframes;
    }

    public static IScriptResource FromPath(IScriptReader reader, string path, bool userLoaded = false) => FromFileInfo(reader, new FileInfo(path), userLoaded);
    public static IScriptResource FromFileInfo(IScriptReader reader, FileInfo file, bool userLoaded = false)
    {
        if (!file.Exists)
            return null;

        var path = file.FullName;
        var origin = userLoaded ? ScriptResourceOrigin.User : ScriptResourceOrigin.Automatic;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var keyframes = reader.Read(stream);
        return new ScriptResource(Path.GetFileName(path), Path.GetDirectoryName(path), keyframes, origin);
    }

    public static IScriptResource FromZipArchiveEntry(IScriptReader reader, string archivePath, ZipArchiveEntry entry, bool userLoaded = false)
    {
        using var stream = entry.Open();

        var origin = userLoaded ? ScriptResourceOrigin.User : ScriptResourceOrigin.Automatic;
        var keyframes = reader.Read(stream);
        return new ScriptResource(entry.Name, archivePath, keyframes, origin);
    }

    public static IScriptResource FromBytes(IScriptReader reader, string name, string source, IEnumerable<byte> bytes, ScriptResourceOrigin origin)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        var keyframes = reader.Read(stream);
        return new ScriptResource(name, source, keyframes, origin);
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