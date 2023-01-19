using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {Position}s]")]
public record Bookmark(string Name, double Position);

public class BookmarkPositionComparer : IComparer<Bookmark>
{
    public static BookmarkPositionComparer Default { get; } = new();

    public int Compare(Bookmark x, Bookmark y)
        => Comparer<double>.Default.Compare(x.Position, y.Position);
}