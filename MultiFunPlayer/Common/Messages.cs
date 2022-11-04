using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.Common;

public enum SettingsAction
{
    Saving,
    Loading
}

internal record SettingsMessage(JObject Settings, SettingsAction Action);
internal record WindowCreatedMessage();

public record MediaSpeedChangedMessage(double Speed);
public record MediaSeekMessage(TimeSpan? Position);
public record MediaPositionChangedMessage(TimeSpan? Position, bool ForceSeek = false);
public record MediaPlayPauseMessage(bool State);
public record MediaPlayingChangedMessage(bool IsPlaying);
public record MediaPathChangedMessage(string Path);
public record MediaDurationChangedMessage(TimeSpan? Duration);

public class ScriptLoadMessage
{
    public Dictionary<DeviceAxis, IScriptResource> Scripts { get; }
    public ScriptLoadMessage(Dictionary<DeviceAxis, IScriptResource> scripts) => Scripts = scripts;
    public ScriptLoadMessage(DeviceAxis axis, IScriptResource script)
        => Scripts = new Dictionary<DeviceAxis, IScriptResource>() { [axis] = script };
}

public class SyncRequestMessage
{
    public List<DeviceAxis> Axes { get; }

    public SyncRequestMessage() => Axes = null;
    public SyncRequestMessage(params DeviceAxis[] axes) : this(axes?.AsEnumerable()) { }
    public SyncRequestMessage(IEnumerable<DeviceAxis> axes) => Axes = axes?.ToList();
}
