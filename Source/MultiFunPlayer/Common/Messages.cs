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
public record MediaPositionChangedMessage(TimeSpan? Position, bool ForceSeek = false);
public record MediaPlayingChangedMessage(bool IsPlaying);
public record MediaPathChangedMessage(string Path, bool ReloadScripts = true);
public record MediaDurationChangedMessage(TimeSpan? Duration);

public record ScriptChangedMessage(DeviceAxis Axis, IScriptResource Script);
public class ChangeScriptMessage
{
    public Dictionary<DeviceAxis, IScriptResource> Scripts { get; }
    public ChangeScriptMessage(Dictionary<DeviceAxis, IScriptResource> scripts) => Scripts = scripts;
    public ChangeScriptMessage(DeviceAxis axis, IScriptResource script) => Scripts = new() { [axis] = script };
    public ChangeScriptMessage(IEnumerable<DeviceAxis> axes, IScriptResource scriptResource) => Scripts = axes.ToDictionary(a => a, _ => scriptResource);
}

public record MediaSeekMessage(TimeSpan Position);
public record MediaPlayPauseMessage(bool ShouldBePlaying);
public record MediaChangePathMessage(string Path);
public record MediaChangeSpeedMessage(double Speed);

public class SyncRequestMessage
{
    public List<DeviceAxis> Axes { get; }

    public SyncRequestMessage() => Axes = null;
    public SyncRequestMessage(params DeviceAxis[] axes) : this(axes?.AsEnumerable()) { }
    public SyncRequestMessage(IEnumerable<DeviceAxis> axes) => Axes = axes?.ToList();
}
