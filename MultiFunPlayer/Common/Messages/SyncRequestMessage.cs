namespace MultiFunPlayer.Common.Messages;

public class SyncRequestMessage
{
    public List<DeviceAxis> Axes { get; }

    public SyncRequestMessage() => Axes = null;
    public SyncRequestMessage(params DeviceAxis[] axes) : this(axes?.AsEnumerable()) { }
    public SyncRequestMessage(IEnumerable<DeviceAxis> axes) => Axes = axes?.ToList();
}
