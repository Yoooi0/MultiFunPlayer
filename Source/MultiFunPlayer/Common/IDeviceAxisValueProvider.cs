namespace MultiFunPlayer.Common;

public interface IDeviceAxisValueProvider
{
    public double GetValue(DeviceAxis axis);

    public (DeviceAxis, DeviceAxisScriptSnapshot) WaitForSnapshotAny(IReadOnlyList<DeviceAxis> axes, CancellationToken cancellationToken);
    public ValueTask<(DeviceAxis, DeviceAxisScriptSnapshot)> WaitForSnapshotAnyAsync(IReadOnlyList<DeviceAxis> axes, CancellationToken cancellationToken);
    public (bool, DeviceAxisScriptSnapshot) WaitForSnapshot(DeviceAxis axis, CancellationToken cancellationToken);
    public ValueTask<(bool, DeviceAxisScriptSnapshot)> WaitForSnapshotAsync(DeviceAxis axis, CancellationToken cancellationToken);
}

public class DeviceAxisScriptSnapshot
{
    public required Keyframe KeyframeFrom { get; init; }
    public required Keyframe KeyframeTo { get; init; }
    public required int IndexFrom { get; init; }
    public required int IndexTo { get; init; }

    public double Duration => KeyframeTo?.Position - KeyframeFrom?.Position ?? double.NaN;
}
