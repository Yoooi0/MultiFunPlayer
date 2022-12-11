using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {StartPosition}s -> {EndPosition}s]")]
public record Chapter(string Name, double StartPosition, double EndPosition);

public class ChapterStartPositionComparer : IComparer<Chapter>
{
    public static ChapterStartPositionComparer Default { get; } = new();

    public int Compare(Chapter x, Chapter y)
        => Comparer<double>.Default.Compare(x.StartPosition, y.StartPosition);
}