using System.Collections;

namespace MultiFunPlayer.Common;

public sealed class KeyframeCollection : IReadOnlyList<Keyframe>
{
    private readonly List<Keyframe> _items;

    public KeyframeCollection() => _items = [];
    public KeyframeCollection(int capacity) => _items = new List<Keyframe>(capacity);

    public void Add(TimeSpan position, double value) => Add(position.TotalSeconds, value);
    public void Add(double position, double value)
    {
        var index = SearchForIndexAfter(position);
        if (this.ValidateIndex(index) && _items[index].Position == position)
            return;

        _items.Insert(index, new Keyframe(position, value));
    }

    public int SearchForIndexBefore(double position) => SearchForIndexAfter(position) - 1;
    public int SearchForIndexAfter(double position)
    {
        if (_items.Count == 0 || position < _items[0].Position)
            return 0;

        if (position > _items[^1].Position)
            return Count;

        var bestIndex = _items.BinarySearch(new Keyframe(position, double.NaN), KeyframePositionComparer.Default);
        if (bestIndex >= 0)
            return bestIndex;

        bestIndex = ~bestIndex;
        return bestIndex == Count ? Count : bestIndex;
    }

    public int AdvanceIndex(int index, double position)
    {
        while (index + 1 >= 0 && index + 1 < Count && this[index + 1].Position <= position)
            index++;
        return index;
    }

    public double Interpolate(int index, double position, InterpolationType interpolationType)
    {
        Keyframe TakeOrExtrapolate(int index, Keyframe p0, Keyframe p1)
            => this.ValidateIndex(index) ? this[index] : new Keyframe(3 * p1.Position - 2 * p0.Position, p1.Value);

        var p0 = this[index + 0];
        var p1 = this[index + 1];

        switch (interpolationType)
        {
            case InterpolationType.Linear:
                return Interpolation.Linear(p0.Position, p0.Value, p1.Position, p1.Value, position);

            case InterpolationType.Pchip:
                {
                    var pm1 = TakeOrExtrapolate(index - 1, p1, p0);
                    var pp1 = TakeOrExtrapolate(index + 2, p0, p1);

                    return Interpolation.Pchip(pm1.Position, pm1.Value, p0.Position, p0.Value, p1.Position, p1.Value, pp1.Position, pp1.Value, position);
                }

            case InterpolationType.Makima:
                {
                    var pm1 = TakeOrExtrapolate(index - 1, p1, p0);
                    var pm2 = TakeOrExtrapolate(index - 2, pm1, p1);
                    var pp1 = TakeOrExtrapolate(index + 2, p0, p1);
                    var pp2 = TakeOrExtrapolate(index + 3, p1, pp1);

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

    public double GapDuration(int index)
    {
        var afterIndex = SkipGap(index);
        if (!this.ValidateIndex(afterIndex) || afterIndex == index)
            return -1;

        return this[afterIndex].Position - this[index].Position;
    }

    public double SegmentDuration(int index)
    {
        if (!this.ValidateIndex(index) || !this.ValidateIndex(index + 1))
            return -1;

        return this[index + 1].Position - this[index].Position;
    }

    private bool IsGapInternal(int index)
    {
        var prev = this[index + 0];
        var next = this[index + 1];

        var adx = Math.Abs(next.Position - prev.Position);
        var ady = Math.Abs(next.Value - prev.Value);

        return ady < 0.001 || adx < 0.001;
    }

    #region IReadOnlyList
    public Keyframe this[int index] => _items[index];
    #endregion

    #region IReadOnlyCollection
    public int Count => _items.Count;
    #endregion

    #region IEnumerable
    public IEnumerator<Keyframe> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    #endregion
}
