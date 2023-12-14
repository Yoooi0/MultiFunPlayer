using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;

namespace MultiFunPlayer.Script.Repository;

internal interface IScriptRepositoryManager
{
    void BeginSearchForScripts(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, Action<Dictionary<DeviceAxis, IScriptResource>> callback, CancellationToken token);
    Task<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, CancellationToken token);
}
