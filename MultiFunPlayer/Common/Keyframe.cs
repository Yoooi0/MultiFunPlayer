using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Position}, {Value}]")]
public class Keyframe
{
    public float Position { get; set; }
    public float Value { get; set; }

    public Keyframe(float position) : this(position, float.NaN) { }
    public Keyframe(float position, float value)
    {
        Position = position;
        Value = value;
    }

    public void Deconstruct(out float position, out float value)
    {
        position = Position;
        value = Value;
    }
}

public class KeyframePositionComparer : IComparer<Keyframe>
{
    public int Compare(Keyframe x, Keyframe y)
        => Comparer<float>.Default.Compare(x.Position, y.Position);
}

public class KeyframeCollection : List<Keyframe>
{
    public bool IsRawCollection { get; init; }

    public int BinarySearch(float position)
    {
        var bestIndex = BinarySearch(new Keyframe(position), new KeyframePositionComparer());
        if (bestIndex >= 0)
        {
            return bestIndex;
        }
        else
        {
            bestIndex = ~bestIndex;
            return bestIndex == Count ? Count : bestIndex - 1;
        }
    }
}
