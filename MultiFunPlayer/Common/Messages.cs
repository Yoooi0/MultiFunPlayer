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

public record MediaSeekMessage(TimeSpan? Position);
public record MediaPlayPauseMessage(bool State);
public record MediaChangePathMessage(string Path);

public class ScriptChangedMessage
{
    public Dictionary<DeviceAxis, IScriptResource> Scripts { get; }
    public ScriptChangedMessage(Dictionary<DeviceAxis, IScriptResource> scripts) => Scripts = scripts;
    public ScriptChangedMessage(DeviceAxis axis, IScriptResource script) => Scripts = new () { [axis] = script };
}

public class SyncRequestMessage
{
    public List<DeviceAxis> Axes { get; }

    public SyncRequestMessage() => Axes = null;
    public SyncRequestMessage(params DeviceAxis[] axes) : this(axes?.AsEnumerable()) { }
    public SyncRequestMessage(IEnumerable<DeviceAxis> axes) => Axes = axes?.ToList();
}
