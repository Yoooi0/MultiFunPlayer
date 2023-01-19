using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Position}, {Value}]")]
public record Keyframe(double Position, double Value);

public class KeyframePositionComparer : IComparer<Keyframe>
{
    public static KeyframePositionComparer Default { get; } = new();

    public int Compare(Keyframe x, Keyframe y)
        => Comparer<double>.Default.Compare(x.Position, y.Position);
}