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

public class LinkedScriptResource : IScriptResource
{
    private readonly IScriptResource _linked;

    public string Name => _linked.Name;
    public string Source => _linked.Source;
    public KeyframeCollection Keyframes => _linked.Keyframes;
    public ChapterCollection Chapters => _linked.Chapters;
    public BookmarkCollection Bookmarks => _linked.Bookmarks;

    public LinkedScriptResource(IScriptResource linked)
    {
        _linked = linked;
    }
}