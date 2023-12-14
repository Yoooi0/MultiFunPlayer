using System.Diagnostics;

namespace MultiFunPlayer.Script;

[DebuggerDisplay("[{Name}: {StartPosition}s -> {EndPosition}s]")]
public sealed record Chapter(string Name, double StartPosition, double EndPosition);

public sealed class ChapterStartPositionComparer : IComparer<Chapter>
{
    public static ChapterStartPositionComparer Default { get; } = new();

    public int Compare(Chapter x, Chapter y)
        => Comparer<double>.Default.Compare(x.StartPosition, y.StartPosition);
}