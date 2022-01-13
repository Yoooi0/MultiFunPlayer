using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Position}, {Value}]")]
public record Keyframe(float Position, float Value);

public class KeyframePositionComparer : IComparer<Keyframe>
{
    public static KeyframePositionComparer Default { get; } = new();

    public int Compare(Keyframe x, Keyframe y)
        => Comparer<float>.Default.Compare(x.Position, y.Position);
}