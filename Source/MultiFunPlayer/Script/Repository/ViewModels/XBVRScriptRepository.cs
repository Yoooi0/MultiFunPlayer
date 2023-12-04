using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Script.Repository;

[DisplayName("XBVR")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class XBVRScriptRepository : AbstractScriptRepository
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonProperty] public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 9999);

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository matcher, CancellationToken token)
    {
        if (Endpoint == null)
            return [];

        if (!TryGetSceneId(out var sceneId))
            return [];

        using var client = NetUtils.CreateHttpClient();
        var result = new Dictionary<DeviceAxis, IScriptResource>();
        var uri = new Uri($"http://{Endpoint.ToUriString()}/api/scene/{sceneId}");
        var response = await client.GetStringAsync(uri, token);

        var metadata = JsonConvert.DeserializeObject<SceneMetadata>(response);
        _ = TryMatchFileSystem() || await TryMatchDms();
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
                var ipOrHost = Endpoint switch
                {
                    IPEndPoint ipEndpoint => ipEndpoint.Address.ToString(),
                    DnsEndPoint dnsEndpoint => dnsEndpoint.Host,
                    _ => null
                };

                if (string.IsNullOrEmpty(ipOrHost))
                    return false;

                var mediaResourceUri = new Uri(mediaResource.IsModified ? mediaResource.ModifiedPath : mediaResource.OriginalPath);
                if (string.Equals(mediaResourceUri.Host, ipOrHost, StringComparison.OrdinalIgnoreCase))
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

        bool TryMatchFileSystem()
        {
            foreach (var videoFile in metadata.Files.Where(f => f.Type == "video"))
            {
                Logger.Debug("Trying to match scripts for video file [Path: {0}, Filename: {1}]", videoFile.Path, videoFile.Filename);
                var searchResult = matcher.SearchForScripts(videoFile.Filename, videoFile.Path, axes);
                foreach(var (axis, resource) in searchResult)
                {
                    if (result.TryGetValue(axis, out var existingResource))
                        Logger.Debug("Overwriting already matched script [From: {0}, To: {1}]", existingResource.Name, resource.Name);

                    result[axis] = resource;
                }
            }

            return result.Count > 0;
        }

        async Task<bool> TryMatchDms()
        {
            var matchedFiles = new Dictionary<DeviceAxis, SceneFile>();
            foreach (var scriptFile in metadata.Files.Where(f => f.Type == "script"))
            {
                Logger.Debug("Trying to match axes for script file [Path: {0}, Filename: {1}]", scriptFile.Path, scriptFile.Filename);
                foreach (var axis in DeviceAxisUtils.FindAxesMatchingName(scriptFile.Filename))
                {
                    Logger.Debug("Matched \"{0}\" to {1} axis", scriptFile.Filename, axis);
                    if (matchedFiles.TryGetValue(axis, out var existingScriptFile))
                        Logger.Debug("Overwriting already matched {0} script file [From: {1}, To: {2}]", axis, existingScriptFile.Filename, scriptFile.Filename);

                    matchedFiles[axis] = scriptFile;
                }
            }

            var result = new Dictionary<DeviceAxis, IScriptResource>();
            foreach (var (axis, script) in matchedFiles)
            {
                var scriptUri = new Uri($"http://{Endpoint.ToUriString()}/api/dms/file/{script.Id}");
                Logger.Trace("Downloading {0} script file [Uri: {1}]", axis, scriptUri);

                var scriptStream = await client.GetStreamAsync(scriptUri, token);
                var readerResult = FunscriptReader.Default.FromStream(script.Filename, uri.ToString(), scriptStream);
                if (!readerResult.IsSuccess)
                    continue;

                if (readerResult.IsMultiAxis)
                    result.Merge(readerResult.Resources);
                else
                    result[axis] = readerResult.Resource;
            }

            return result.Count > 0;
        }
    }

    private sealed record SceneMetadata([JsonProperty("file")] List<SceneFile> Files);
    private sealed record SceneFile(int Id, string Path, string Filename, string Type);
}