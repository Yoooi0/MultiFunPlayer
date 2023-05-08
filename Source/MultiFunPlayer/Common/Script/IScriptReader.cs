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
    ScriptReaderResult FromStream(string name, string source, Stream stream);
    ScriptReaderResult FromPath(string path);
    ScriptReaderResult FromFileInfo(FileInfo file);
}

public sealed class ScriptReaderResult
{
    public IScriptResource Resource { get; }
    public Dictionary<DeviceAxis, IScriptResource> Resources { get; }

    public bool IsSuccess => Resource != null || Resources != null;
    public bool IsMultiAxis => Resources?.Count > 0;

    private ScriptReaderResult() { }
    private ScriptReaderResult(IScriptResource resource) => Resource = resource;
    private ScriptReaderResult(Dictionary<DeviceAxis, IScriptResource> resources) => Resources = resources;

    public static ScriptReaderResult FromFailure() => new();
    public static ScriptReaderResult FromSuccess(IScriptResource resource) => new(resource);
    public static ScriptReaderResult FromSuccess(Dictionary<DeviceAxis, IScriptResource> resources) => new(resources);
}

public abstract class AbstractScriptReader : IScriptReader
{
    public abstract ScriptReaderResult FromStream(string name, string source, Stream stream);

    public ScriptReaderResult FromPath(string path) => FromFileInfo(new FileInfo(path));
    public ScriptReaderResult FromFileInfo(FileInfo file)
    {
        if (!file.Exists)
            return ScriptReaderResult.FromFailure();

        using var stream = file.OpenRead();
        return FromStream(file.Name, file.DirectoryName, stream);
    }
}

public abstract class AbstractTextScriptReader : AbstractScriptReader
{
    public abstract ScriptReaderResult FromStream(string name, string source, TextReader stream);

    public override ScriptReaderResult FromStream(string name, string source, Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        return FromStream(name, source, streamReader);
    }

    public ScriptReaderResult FromBytes(string name, string source, ReadOnlySpan<byte> bytes, Encoding encoding)
        => FromText(name, source, encoding.GetString(bytes));

    public ScriptReaderResult FromText(string name, string source, string text)
    {
        using var stream = new StringReader(text);
        return FromStream(name, source, stream);
    }
}

public class FunscriptReader : AbstractTextScriptReader
{
    public static FunscriptReader Default { get; } = new FunscriptReader();

    public override ScriptReaderResult FromStream(string name, string source, TextReader stream)
    {
        using var jsonReader = new JsonTextReader(stream);
        var serializer = JsonSerializer.CreateDefault();

        var script = serializer.Deserialize<Script>(jsonReader);
        var hasActions = script.Actions?.Count > 0;
        var hasAxes = script.Axes?.Count > 0;
        if (!hasActions && !hasAxes)
            return ScriptReaderResult.FromFailure();

        var resource = CreateResource(script.Actions);
        if (!hasAxes)
            return ScriptReaderResult.FromSuccess(resource);

        var resources = new Dictionary<DeviceAxis, IScriptResource>();
        if (hasActions && DeviceAxis.TryParse("L0", out var strokeAxis))
            resources[strokeAxis] = resource;

        foreach(var scriptAxis in script.Axes)
        {
            if (!DeviceAxis.TryParse(scriptAxis.Id, out var axis))
                continue;

            resources[axis] = CreateResource(scriptAxis.Actions);
        }

        return ScriptReaderResult.FromSuccess(resources);

        IScriptResource CreateResource(List<Action> actions) => new ScriptResource()
        {
            Name = name,
            Source = source,
            Keyframes = CreateKeyframeCollection(actions),
            Chapters = CreateChapterCollection(script.Metadata),
            Bookmarks = CreateBookmarkCollection(script.Metadata)
        };

        static ChapterCollection CreateChapterCollection(Metadata metadata)
        {
            if (!(metadata?.Chapters?.Count > 0))
                return null;

            var chapters = new ChapterCollection(metadata.Chapters.Count);
            foreach (var chapter in metadata.Chapters)
                if (chapter.StartTime >= TimeSpan.Zero)
                    chapters.Add(chapter.Name, chapter.StartTime, chapter.EndTime);

            return chapters;
        }

        static BookmarkCollection CreateBookmarkCollection(Metadata metadata)
        {
            if (!(metadata?.Bookmarks?.Count > 0))
                return null;

            var bookmarks = new BookmarkCollection(metadata.Bookmarks.Count);
            foreach (var bookmark in metadata.Bookmarks)
                if (bookmark.Time >= TimeSpan.Zero)
                    bookmarks.Add(bookmark.Name, bookmark.Time);

            return bookmarks;
        }

        static KeyframeCollection CreateKeyframeCollection(List<Action> actions)
        {
            if (!(actions?.Count > 0))
                return null;

            var keyframes = new KeyframeCollection(actions.Count);
            foreach (var action in actions)
            {
                var position = action.At / 1000;
                if (position < 0)
                    continue;

                var value = MathUtils.Clamp01(action.Pos / 100);
                keyframes.Add(position, value);
            }

            return keyframes;
        }
    }

    private record Script(List<Action> Actions, List<ScriptAxis> Axes, Metadata Metadata);
    private record Action(double At, double Pos);
    private record ScriptAxis(string Id, List<Action> Actions);
    private record Metadata(List<Chapter> Chapters, List<Bookmark> Bookmarks);
    private record Chapter(string Name, TimeSpan StartTime, TimeSpan EndTime);
    private record Bookmark(string Name, TimeSpan Time);
}

public class CsvReader : AbstractTextScriptReader
{
    public static CsvReader Default { get; } = new CsvReader();

    public override ScriptReaderResult FromStream(string name, string source, TextReader stream)
    {
        var keyframes = new KeyframeCollection();

        while (true)
        {
            var line = stream.ReadLine();
            if (line == null)
                break;

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

        if (keyframes.Count == 0)
            return ScriptReaderResult.FromFailure();

        return ScriptReaderResult.FromSuccess(new ScriptResource()
        {
            Name = name,
            Source = source,
            Keyframes = keyframes
        });
    }
}