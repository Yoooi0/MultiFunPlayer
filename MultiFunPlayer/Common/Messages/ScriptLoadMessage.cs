namespace MultiFunPlayer.Common.Messages;

public class ScriptLoadMessage
{
    public Dictionary<DeviceAxis, IScriptFile> Scripts { get; }
    public ScriptLoadMessage(Dictionary<DeviceAxis, IScriptFile> scripts) => Scripts = scripts;
    public ScriptLoadMessage(DeviceAxis axis, IScriptFile script)
        => Scripts = new Dictionary<DeviceAxis, IScriptFile>() { [axis] = script };
}
