namespace MultiFunPlayer.Common;

internal interface IDeviceAxisValueProvider
{
    public double GetValue(DeviceAxis axis);

    public void BeginSnapshotPolling(object context);
    public void EndSnapshotPolling(object context);

    public (DeviceAxis, DeviceAxisScriptSnapshot) WaitForSnapshotAny(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken);
    public ValueTask<(DeviceAxis, DeviceAxisScriptSnapshot)> WaitForSnapshotAnyAsync(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken);
    public (bool, DeviceAxisScriptSnapshot) WaitForSnapshot(DeviceAxis axis, object context, CancellationToken cancellationToken);
    public ValueTask<(bool, DeviceAxisScriptSnapshot)> WaitForSnapshotAsync(DeviceAxis axis, object context, CancellationToken cancellationToken);
}

internal sealed class DeviceAxisScriptSnapshot
{
    public required Keyframe KeyframeFrom { get; init; }
    public required Keyframe KeyframeTo { get; init; }
    public required int IndexFrom { get; init; }
    public required int IndexTo { get; init; }

    public double Duration => KeyframeTo?.Position - KeyframeFrom?.Position ?? double.NaN;
}