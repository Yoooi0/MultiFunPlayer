using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.Script.Repository;

internal interface IScriptRepository : INotifyPropertyChanged
{
    string Name { get; }
    bool Enabled { get; }

    void HandleSettings(JObject settings, SettingsAction action);

    ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token);
}

internal interface ILocalScriptRepository
{
    Dictionary<DeviceAxis, IScriptResource> SearchForScripts(string mediaName, string mediaSource, IEnumerable<DeviceAxis> axes);
}

[JsonObject(MemberSerialization.OptIn)]
internal abstract class AbstractScriptRepository : Screen, IScriptRepository
{
    public string Name { get; }
    [JsonProperty] public bool Enabled { get; set; }

    protected AbstractScriptRepository()
    {
        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    }

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
            settings.MergeAll(JObject.FromObject(this));
        else if (action == SettingsAction.Loading)
            settings.Populate(this);
    }

    public abstract ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token);
}
