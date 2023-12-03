using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.Script.Repository;

internal interface IScriptRepository
{
    string Name { get; }

    void HandleSettings(JObject settings, SettingsAction action);

    ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token);
}

internal interface ILocalScriptRepository
{
    Dictionary<DeviceAxis, IScriptResource> SearchForScripts(string mediaName, string mediaSource, IEnumerable<DeviceAxis> axes);
}

internal abstract class AbstractScriptRepository : Screen, IScriptRepository
{
    public string Name { get; }

    protected AbstractScriptRepository()
    {
        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    }

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
            settings.Merge(JObject.FromObject(this), new JsonMergeSettings() { MergeArrayHandling = MergeArrayHandling.Replace });
        else if (action == SettingsAction.Loading)
            settings.Populate(this);
    }

    public abstract ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token);
}
