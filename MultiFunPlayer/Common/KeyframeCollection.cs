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
        static float Distance(Keyframe p0, Keyframe p1)
            => MathF.Sqrt((p1.Position - p0.Position) * (p1.Position - p0.Position) + (p1.Value - p0.Value) * (p1.Value - p0.Value));

        Keyframe TakeOrExtrapolateRight(int index, Keyframe prev0, Keyframe prev1)
            => index < Count ? this[index] : new Keyframe(prev1.Position + Distance(prev0, prev1) * 2, prev1.Value);

        Keyframe TakeOrExtrapolateLeft(int index, Keyframe next1, Keyframe next0)
            => index >= 0 ? this[index] : new Keyframe(next1.Position - Distance(next1, next0) * 2, next1.Value);

        if (IsRawCollection)
            interpolationType = InterpolationType.Linear;

        var p0 = this[index + 0];
        var p1 = this[index + 1];

        switch (interpolationType)
        {
            case InterpolationType.Linear:
                return Interpolation.Linear(p0.Position, p0.Value, p1.Position, p1.Value, position);

            case InterpolationType.Pchip:
                {
                    var pm1 = TakeOrExtrapolateLeft(index - 1, p1, p0);
                    var pp1 = TakeOrExtrapolateRight(index + 2, p0, p1);

                    return Interpolation.Pchip(pm1.Position, pm1.Value, p0.Position, p0.Value, p1.Position, p1.Value, pp1.Position, pp1.Value, position);
                }

            case InterpolationType.Makima:
                {
                    var pm1 = TakeOrExtrapolateLeft(index - 1, p1, p0);
                    var pm2 = TakeOrExtrapolateLeft(index - 2, pm1, p1);
                    var pp1 = TakeOrExtrapolateRight(index + 2, p0, p1);
                    var pp2 = TakeOrExtrapolateRight(index + 3, p1, pp1);

                    return Interpolation.Makima(pm2.Position, pm2.Value, pm1.Position, pm1.Value, p0.Position, p0.Value, p1.Position, p1.Value, pp1.Position, pp1.Value, pp2.Position, pp2.Value, position);
                }

            case InterpolationType.Step:
                return Interpolation.Step(p0.Position, p0.Value, position);

            default:
                throw new NotSupportedException();
        }
    }

    public int SkipGap(int index)
    {
        if (!this.ValidateIndex(index) || !this.ValidateIndex(index + 1))
            return -1;

        while (index + 1 < Count && IsGapInternal(index))
            index++;

        return index;
    }

    public bool IsGap(int index)
    {
        if (!this.ValidateIndex(index) || !this.ValidateIndex(index + 1))
            return false;

        return IsGapInternal(index);
    }

    public float SegmentDuration(int index)
    {
        if (!this.ValidateIndex(index) || !this.ValidateIndex(index + 1))
            return -1;

        return this[index + 1].Position - this[index].Position;
    }

    private bool IsGapInternal(int index)
    {
        var prev = this[index + 0];
        var next = this[index + 1];

        var adx = MathF.Abs(next.Position - prev.Position);
        var ady = MathF.Abs(next.Value - prev.Value);

        return ady < 0.001f || adx < 0.001f;
    }
}
