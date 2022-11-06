using System.Collections;

namespace MultiFunPlayer.Common;

public class KeyframeCollection : IList<Keyframe>, IReadOnlyList<Keyframe>, IList, ICollection
{
    private readonly List<Keyframe> _items;

    public KeyframeCollection() => _items = new List<Keyframe>();
    public KeyframeCollection(int capacity) => _items = new List<Keyframe>(capacity);

    public KeyframeCollection(IEnumerable<Keyframe> collection)
    {
        _items = new List<Keyframe>();
        AddRange(collection);
    }

    public bool IsRawCollection { get; init; }

    public void AddRange(IEnumerable<Keyframe> items)
    {
        foreach (var item in items)
            Add(item);
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
        while (index + 1 >= 0 && index + 1 < Count && this[index + 1].Position < position)
            index++;
        return index;
    }

    public double Interpolate(int index, double position, InterpolationType interpolationType)
    {
        static double Distance(Keyframe p0, Keyframe p1)
            => Math.Sqrt((p1.Position - p0.Position) * (p1.Position - p0.Position) + (p1.Value - p0.Value) * (p1.Value - p0.Value));

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

    #region IList<T>
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public Keyframe this[int index] { get => _items[index]; }
    Keyframe IList<Keyframe>.this[int index] { get => _items[index]; set => Add(value); }

    public int IndexOf(Keyframe item) => _items.IndexOf(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public void Add(Keyframe item) => _items.Insert(SearchForIndexAfter(item.Position), item);
    public void Clear() => _items.Clear();
    public bool Contains(Keyframe item) => _items.Contains(item);
    public void CopyTo(Keyframe[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(Keyframe item) => _items.Remove(item);

    void IList<Keyframe>.Insert(int index, Keyframe item) => Add(item);
    #endregion

    #region IList
    bool IList.IsFixedSize => false;
    object IList.this[int index] { get => _items[index]; set => Add((Keyframe)value); }

    int IList.Add(object value)
    {
        var item = (Keyframe)value;
        var index = SearchForIndexAfter(item.Position);

        _items.Insert(index, item);
        return index;
    }

    bool IList.Contains(object value) => Contains((Keyframe)value);
    int IList.IndexOf(object value) => IndexOf((Keyframe)value);
    void IList.Insert(int index, object value) => Add((Keyframe)value);
    void IList.Remove(object value) => Remove((Keyframe)value);

    #endregion

    #region IEnumerable
    public IEnumerator<Keyframe> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    #endregion

    #region ICollection
    bool ICollection.IsSynchronized => false;
    public object SyncRoot => this;
    void ICollection.CopyTo(Array array, int index) => ((IList)_items).CopyTo(array, index);
    #endregion
}
