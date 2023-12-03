using System.Diagnostics;

namespace MultiFunPlayer.Script;

[DebuggerDisplay("[{Name}: {Position}s]")]
public sealed record Bookmark(string Name, double Position);

public sealed class BookmarkPositionComparer : IComparer<Bookmark>
{
    public static BookmarkPositionComparer Default { get; } = new();

    public int Compare(Bookmark x, Bookmark y)
        => Comparer<double>.Default.Compare(x.Position, y.Position);
}