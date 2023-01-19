using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MultiFunPlayer.Common;

public enum ScriptType
{
    Funscript,
    Csv
}

public interface IScriptReader
{
    IScriptResource FromStream(string name, string source, Stream stream);
    IScriptResource FromPath(string path);
    IScriptResource FromFileInfo(FileInfo file);
    IScriptResource FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry);
    IScriptResource FromBytes(string name, string source, IEnumerable<byte> bytes);
}

public abstract class AbstractScriptReader : IScriptReader
{
    public abstract IScriptResource FromStream(string name, string source, Stream stream);

    public IScriptResource FromPath(string path) => FromFileInfo(new FileInfo(path));
    public IScriptResource FromFileInfo(FileInfo file)
    {
        if (!file.Exists)
            return null;

        var path = file.FullName;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        return FromStream(Path.GetFileName(path), Path.GetDirectoryName(path), stream);
    }

    public IScriptResource FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return FromStream(entry.Name, archivePath, stream);
    }

    public IScriptResource FromBytes(string name, string source, IEnumerable<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        return FromStream(name, source, stream);
    }
}

public class FunscriptReaderSettings { }

public class FunscriptReader : AbstractScriptReader
{
    public static FunscriptReader Default { get; } = new FunscriptReader();

    public FunscriptReader() : this(new FunscriptReaderSettings()) { }
    public FunscriptReader(FunscriptReaderSettings settings) { }

    public override IScriptResource FromStream(string name, string source, Stream stream)
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
            keyframes.Add(position, value);
        }

        var metadata = script.Metadata;
        var chapters = default(ChapterCollection);
        var bookmarks = default(BookmarkCollection);

        if (metadata?.Chapters?.Count > 0)
        {
            chapters = new ChapterCollection(metadata.Chapters.Count);
            foreach (var chapter in metadata.Chapters)
                if (chapter.StartTime >= TimeSpan.Zero)
                    chapters.Add(chapter.Name, chapter.StartTime, chapter.EndTime);
        }

        if (metadata?.Bookmarks?.Count > 0)
        {
            bookmarks = new BookmarkCollection(metadata.Bookmarks.Count);
            foreach (var bookmark in metadata.Bookmarks)
                if (bookmark.Time >= TimeSpan.Zero)
                    bookmarks.Add(bookmark.Name, bookmark.Time);
        }

        return new ScriptResource()
        {
            Name = name,
            Source = source,
            Keyframes = keyframes,
            Chapters = chapters,
            Bookmarks = bookmarks
        };
    }

    private record Script(List<Action> Actions, Metadata Metadata);
    private record Action(double At, double Pos);
    private record Metadata(List<Chapter> Chapters, List<Bookmark> Bookmarks);
    private record Chapter(string Name, TimeSpan StartTime, TimeSpan EndTime);
    private record Bookmark(string Name, TimeSpan Time);
}

public class CsvReaderSettings { }

public class CsvReader : AbstractScriptReader
{
    public static CsvReader Default { get; } = new CsvReader();

    public CsvReader() : this(new CsvReaderSettings()) { }
    public CsvReader(CsvReaderSettings settings) { }

    public override IScriptResource FromStream(string name, string source, Stream stream)
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
            keyframes.Add(position, value);
        }

        return new ScriptResource()
        {
            Name = name,
            Source = source,
            Keyframes = keyframes
        };
    }
}