namespace MultiFunPlayer.Common.Messages;

public class ScriptLoadMessage
{
    public Dictionary<DeviceAxis, IScriptResource> Scripts { get; }
    public ScriptLoadMessage(Dictionary<DeviceAxis, IScriptResource> scripts) => Scripts = scripts;
    public ScriptLoadMessage(DeviceAxis axis, IScriptResource script)
        => Scripts = new Dictionary<DeviceAxis, IScriptResource>() { [axis] = script };
}
