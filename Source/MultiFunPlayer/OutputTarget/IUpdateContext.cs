using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Stylet;
using System.Windows.Media;

namespace MultiFunPlayer.OutputTarget;

internal interface IUpdateContext;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal abstract class AbstractPolledUpdateContext : PropertyChangedBase, IUpdateContext
{
    private readonly Dictionary<DeviceAxis, double> _previousDuration = [];
    private readonly Queue<double> _errors = [];

    public double AverageUpdateError { get; private set; }

    public void UpdateStats(DeviceAxis axis, DeviceAxisScriptSnapshot snapshot, double elapsed)
    {
        if (!_previousDuration.TryGetValue(axis, out var previousDuration) || !double.IsFinite(previousDuration))
        {
            _previousDuration[axis] = snapshot.Duration;
            return;
        }

        _errors.Enqueue(elapsed - previousDuration);
        while(_errors.Count > 25)
            _errors.Dequeue();

        AverageUpdateError = _errors.Average() * 1000;
        _previousDuration[axis] = snapshot.Duration;
    }
}

internal class ThreadPolledUpdateContext : AbstractPolledUpdateContext;
internal class AsyncPolledUpdateContext : AbstractPolledUpdateContext;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal abstract class AbstractFixedUpdateContext : PropertyChangedBase, IUpdateContext
{
    private DoubleCollection _updateIntervalTicks;
    private double _statsTime;
    private int _statsCount;
    private int _statsJitter = int.MinValue;

    [JsonProperty] public int UpdateInterval { get; set; } = 10;

    public int MinimumUpdateInterval { get; init; } = 3;
    public int MaximumUpdateInterval { get; init; } = 33;
    public int AverageUpdateRate { get; private set; }
    public int UpdateRateJitter { get; private set; }
    public DoubleCollection UpdateIntervalTicks
    {
        get
        {
            if (_updateIntervalTicks == null)
            {
                _updateIntervalTicks = [];
                for (var i = MaximumUpdateInterval; i >= MinimumUpdateInterval; i--)
                    _updateIntervalTicks.Add(i);
            }

            return _updateIntervalTicks;
        }
    }

    public void UpdateStats(double elapsed)
    {
        _statsTime += elapsed;
        _statsCount++;

        var updateRateDiff = (int)Math.Round(Math.Abs(1000d / UpdateInterval - 1 / elapsed));
        _statsJitter = Math.Max(_statsJitter, updateRateDiff);

        if (_statsTime > 0.25)
        {
            UpdateRateJitter = _statsJitter;
            AverageUpdateRate = (int)Math.Round(1 / (_statsTime / _statsCount));
            _statsTime = 0;
            _statsCount = 0;
            _statsJitter = int.MinValue;
        }
    }
}

internal class ThreadFixedUpdateContext : AbstractFixedUpdateContext
{
    [JsonProperty] public bool UsePreciseSleep { get; set; }
}

internal class AsyncFixedUpdateContext : AbstractFixedUpdateContext;

internal sealed class TCodeThreadFixedUpdateContext : ThreadFixedUpdateContext
{
    [JsonProperty] public bool OffloadElapsedTime { get; set; } = true;
    [JsonProperty] public bool SendDirtyValuesOnly { get; set; } = true;
}

internal sealed class TCodeAsyncFixedUpdateContext : AsyncFixedUpdateContext
{
    [JsonProperty] public bool OffloadElapsedTime { get; set; } = true;
    [JsonProperty] public bool SendDirtyValuesOnly { get; set; } = true;
}