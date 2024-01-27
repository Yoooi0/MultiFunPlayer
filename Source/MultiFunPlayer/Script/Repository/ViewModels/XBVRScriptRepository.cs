using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using NLog;
using System.ComponentModel;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Script.Repository.ViewModels;

[DisplayName("XBVR")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class XBVRScriptRepository : AbstractScriptRepository
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonProperty] public Uri ServerBaseUri { get; set; } = new Uri("http://127.0.0.1:9999");
    [JsonProperty] public XBVRVideoMatchType VideoMatchType { get; set; } = XBVRVideoMatchType.UseFirstMatchOnly;
    [JsonProperty] public XBVRScriptMatchType ScriptMatchType { get; set; } = XBVRScriptMatchType.MatchAllUseFirst;

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
    {
        if (ServerBaseUri == null)
            return [];

        if (!TryGetSceneId(out var sceneId))
            return [];

        using var client = NetUtils.CreateHttpClient();
        var result = new Dictionary<DeviceAxis, IScriptResource>();
        var uri = new Uri(ServerBaseUri, $"/api/scene/{sceneId}");
        var response = await client.GetStringAsync(uri, token);

        var metadata = JsonConvert.DeserializeObject<SceneMetadata>(response);
        _ = TryMatchLocal(metadata, axes, result, localRepository) || await TryMatchDms(metadata, axes, result, client, token);
        return result;

        bool TryGetSceneId(out object sceneId)
        {
            sceneId = null;
            if (mediaResource.IsPath)
            {
                var match = Regex.Match(mediaResource.Name, @"^(?<id>\d+) - .+");
                if (!match.Success)
                    return false;

                sceneId = match.Groups["id"].Value;
                return true;
            }
            else if (mediaResource.IsUrl)
            {
                var mediaResourceUri = new Uri(mediaResource.IsModified ? mediaResource.ModifiedPath : mediaResource.OriginalPath);
                if (string.Equals(mediaResourceUri.Host, ServerBaseUri.Host, StringComparison.OrdinalIgnoreCase))
                    return false;

                // <endpoint>/res?scene=<sceneId>
                var match = Regex.Match(mediaResourceUri.Query, "scene=(?<id>.+?)(?>$|&)");
                if (match.Success)
                {
                    sceneId = match.Groups["id"].Value;
                    return true;
                }

                // <endpoint>/api/dms/file/<sceneId>
                match = Regex.Match(mediaResourceUri.Query, @"api\/dms\/file\/(?<id>\d+)");
                if (match.Success)
                {
                    sceneId = match.Groups["id"].Value;
                    return true;
                }
            }

            return false;
        }
    }

    private bool TryMatchLocal(SceneMetadata metadata, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, ILocalScriptRepository localRepository)
    {
        if (metadata?.Files == null)
            return false;

        foreach (var videoFile in metadata.Files.Where(f => f.Type == "video"))
        {
            Logger.Debug("Trying to match scripts for video file [Path: {0}, Filename: {1}]", videoFile.Path, videoFile.Filename);
            var searchResult = localRepository.SearchForScripts(videoFile.Filename, videoFile.Path, axes);
            if (searchResult.Count == 0)
                continue;

            if (VideoMatchType == XBVRVideoMatchType.UseFirstMatchOnly)
            {
                result.Merge(searchResult);
                return true;
            }

            foreach (var (axis, resource) in searchResult)
            {
                if (VideoMatchType == XBVRVideoMatchType.MatchAllOverwrite && result.TryGetValue(axis, out var existingResource))
                    Logger.Debug("Overwriting already matched script [From: {0}, To: {1}]", existingResource.Name, resource.Name);

                var validMatch = (VideoMatchType == XBVRVideoMatchType.MatchAllUseFirst && !result.ContainsKey(axis))
                              || (VideoMatchType == XBVRVideoMatchType.MatchAllOverwrite);

                if (validMatch)
                {
                    Logger.Debug("Matched \"{0}\" to {1} axis", resource.Name, axis);
                    result[axis] = resource;
                }
            }
        }

        return result.Count > 0;
    }

    private async Task<bool> TryMatchDms(SceneMetadata metadata, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, HttpClient client, CancellationToken token)
    {
        if (metadata?.Files == null)
            return false;

        var matchedFiles = new Dictionary<DeviceAxis, SceneFile>();
        foreach (var scriptFile in metadata.Files.Where(f => f.Type == "script"))
        {
            Logger.Debug("Trying to match axes for script file [Path: {0}, Filename: {1}, IsSelected: {2}]", scriptFile.Path, scriptFile.Filename, scriptFile.IsSelected);
            foreach (var axis in DeviceAxisUtils.FindAxesMatchingName(axes, scriptFile.Filename))
            {
                if (ScriptMatchType == XBVRScriptMatchType.MatchAllOverwrite && matchedFiles.TryGetValue(axis, out var existingScriptFile))
                    Logger.Debug("Overwriting already matched {0} script file [From: {1}, To: {2}]", axis, existingScriptFile.Filename, scriptFile.Filename);

                var validMatch = (ScriptMatchType == XBVRScriptMatchType.MatchSelectedOnly && scriptFile.IsSelected)
                              || (ScriptMatchType == XBVRScriptMatchType.MatchAllUseFirst && !matchedFiles.ContainsKey(axis))
                              || (ScriptMatchType == XBVRScriptMatchType.MatchAllOverwrite);

                if (validMatch)
                {
                    Logger.Debug("Matched \"{0}\" to {1} axis", scriptFile.Filename, axis);
                    matchedFiles[axis] = scriptFile;
                }
            }
        }

        foreach (var (axis, script) in matchedFiles)
        {
            var scriptUri = new Uri(ServerBaseUri, $"/api/dms/file/{script.Id}");
            Logger.Trace("Downloading {0} script file [Uri: {1}]", axis, scriptUri);

            var scriptStream = await client.GetStreamAsync(scriptUri, token);
            var readerResult = FunscriptReader.Default.FromStream(script.Filename, scriptUri.ToString(), scriptStream);
            if (!readerResult.IsSuccess)
                continue;

            if (readerResult.IsMultiAxis)
                result.Merge(readerResult.Resources);
            else
                result[axis] = readerResult.Resource;
        }

        return result.Count > 0;
    }

    private sealed record SceneMetadata([JsonProperty("file")] List<SceneFile> Files);
    private sealed record SceneFile(int Id, string Path, string Filename, string Type, [JsonProperty("is_selected_script")] bool IsSelected);
}

internal enum XBVRScriptMatchType
{
    [Description("Try to match all scripts, for each axis only use the first matched script")]
    MatchAllUseFirst,
    [Description("Try to match all scripts, overwrite if multiple scripts match an axis")]
    MatchAllOverwrite,
    [Description("Try to only match scripts selected in XBVR, overwrite if multiple scripts match an axis")]
    MatchSelectedOnly
}

internal enum XBVRVideoMatchType
{
    [Description("Use first video that matches at least one axis")]
    UseFirstMatchOnly,
    [Description("Try to match all videos, for each axis use first matched script")]
    MatchAllUseFirst,
    [Description("Try to match all videos, overwrite if multiple scripts match an axis")]
    MatchAllOverwrite
}