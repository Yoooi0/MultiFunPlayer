namespace MultiFunPlayer.Common;

public class KeyframeCollection : List<Keyframe>
{
    public bool IsRawCollection { get; init; }

    public int BinarySearch(float position)
    {
        var bestIndex = BinarySearch(new Keyframe(position, float.NaN), KeyframePositionComparer.Default);
        if (bestIndex >= 0)
            return bestIndex;

        bestIndex = ~bestIndex;
        return bestIndex == Count ? Count : bestIndex - 1;
    }

    public int AdvanceIndex(int index, float position)
    {
        while (index + 1 >= 0 && index + 1 < Count && this[index + 1].Position < position)
            index++;
        return index;
    }

    public float Interpolate(int index, float position, InterpolationType interpolationType)
    {
        var pointCount = Interpolation.RequiredPointCount(interpolationType);
        if (pointCount == 4 && (IsRawCollection || index == 0 || index + 2 == Count))
            pointCount = 2;

        if (pointCount == 4)
        {
            var p0 = this[index - 1];
            var p1 = this[index + 0];
            var p2 = this[index + 1];
            var p3 = this[index + 2];

            return MathUtils.Interpolate(p0.Position, p0.Value, p1.Position, p1.Value, p2.Position, p2.Value, p3.Position, p3.Value,
                                         position, interpolationType);
        }
        else if (pointCount == 2)
        {
            var p0 = this[index + 0];
            var p1 = this[index + 1];

            return MathUtils.Interpolate(p0.Position, p0.Value, p1.Position, p1.Value, position, interpolationType);
        }
        else
        {
            var p0 = this[index];

            return MathUtils.Interpolate(p0.Position, p0.Value, position, interpolationType);
        }
    }

    public int SkipGap(int index)
    {
        if (!this.ValidateIndex(index))
            return -1;

        for(int i = index, j = index + 1; j < Count; i = j++)
        {
            var prev = this[i];
            var next = this[j];

            var adx = MathF.Abs(next.Position - prev.Position);
            var ady = MathF.Abs(next.Value - prev.Value);
            if (ady < 0.001f || adx < 0.001f)
                continue;

            return i;
        }

        return -1;
    }
}
