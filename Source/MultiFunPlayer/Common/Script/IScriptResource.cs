namespace MultiFunPlayer.Common;

public interface IScriptResource
{
    string Name { get; }
    string Source { get; }
    KeyframeCollection Keyframes { get; }
    ChapterCollection Chapters { get; }
    BookmarkCollection Bookmarks { get; }
}

public class ScriptResource : IScriptResource
{
    public string Name { get; init; }
    public string Source { get; init; }
    public KeyframeCollection Keyframes { get; init; }
    public ChapterCollection Chapters { get; init; }
    public BookmarkCollection Bookmarks { get; init; }

    public static LinkedScriptResource LinkTo(IScriptResource other) => other != null ? new LinkedScriptResource(other) : null;
}

public class LinkedScriptResource(IScriptResource linked) : IScriptResource
{
    public string Name => linked.Name;
    public string Source => linked.Source;
    public KeyframeCollection Keyframes => linked.Keyframes;
    public ChapterCollection Chapters => linked.Chapters;
    public BookmarkCollection Bookmarks => linked.Bookmarks;
}