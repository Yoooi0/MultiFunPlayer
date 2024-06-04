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
    [JsonProperty] public XBVRLocalMatchType LocalMatchType { get; set; } = XBVRLocalMatchType.MatchToCurrentFile;
    [JsonProperty] public XBVRDmsMatchType DmsMatchType { get; set; } = XBVRDmsMatchType.MatchToCurrentFile;

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
    {
        if (ServerBaseUri == null)
            return [];

        var client = default(HttpClient);
        try
        {
            Logger.Trace("Searching for ids in \"{0}\"", mediaResource.Path);
            var (sceneId, fileId) = await TryGetIds(mediaResource);

            Logger.Debug("Found ids [SceneId: \"{0}\", FileId: \"{1}\"]", sceneId, fileId);
            if (fileId != null && sceneId == null)
            {
                Logger.Debug("Trying to reverse lookup SceneId from FileId");

                var fileMetadata = await GetFileMetadataAsync(fileId);
                if (fileMetadata?.Type == "video")
                    sceneId = fileMetadata?.SceneId;

                Logger.Trace("Set SceneId to \"{0}\"", sceneId);
            }

            if (sceneId is null or 0 || fileId is null or 0)
                return [];

            var sceneMetadata = await GetSceneMetadataAsync(sceneId);
            if (sceneMetadata?.Files == null || sceneMetadata.Files.Count == 0)
                return [];

            var currentFile = sceneMetadata.Files.Find(f => f.Id == fileId);
            if (currentFile == null)
                return [];

            var result = new Dictionary<DeviceAxis, IScriptResource>();
            if (!TryMatchLocal(currentFile, axes, result, localRepository))
                await TryMatchDms(sceneMetadata, currentFile, axes, result, client, token);

            return result;
        }
        finally
        {
            client?.Dispose();
        }

        async Task<SceneMetadata> GetSceneMetadataAsync(int? sceneId)
        {
            if (sceneId is null or <= 0)
                return null;

            client ??= NetUtils.CreateHttpClient();

            var uri = new Uri(ServerBaseUri, $"/api/scene/{sceneId}");
            var response = await client.GetStringAsync(uri, token);

            Logger.Trace("Received scene content \"{0}\"", response);
            return JsonConvert.DeserializeObject<SceneMetadata>(response);
        }

        async Task<FileMetadata> GetFileMetadataAsync(int? fileId)
        {
            if (fileId is null or <= 0)
                return null;

            client ??= NetUtils.CreateHttpClient();

            var uri = new Uri(ServerBaseUri, $"/api/files/file/{fileId}");
            var response = await client.GetStringAsync(uri, token);

            Logger.Trace("Received file content \"{0}\"", response);
            return JsonConvert.DeserializeObject<FileMetadata>(response);
        }
    }

    private async ValueTask<(int? SceneId, int? FileId)> TryGetIds(MediaResourceInfo mediaResource)
    {
        if (!mediaResource.IsUrl)
            return (null, null);

        var mediaResourceUri = new Uri(mediaResource.Path);
        if (!await PointsToTheSameEndpoint(mediaResourceUri, ServerBaseUri))
        {
            Logger.Debug("Ignoring \"{0}\" resource because it does not point to \"{1}\"", mediaResource.Path, ServerBaseUri);
            return (null, null);
        }

        var pathAndQuery = mediaResourceUri.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);

        // DLNA: <endpoint>/res?scene=<sceneId>&file=<fileId>
        var dlnaSceneMatch = Regex.Match(pathAndQuery, @"res\?.*scene=(?<id>\d+)(?>$|&)");
        if (dlnaSceneMatch.Success)
        {
            var sceneId = int.Parse(dlnaSceneMatch.Groups["id"].Value);
            var dlnaFileMatch = Regex.Match(pathAndQuery, @"res\?.*file=(?<id>\d+)(?>$|&)");
            var fileId = dlnaFileMatch.Success ? int.Parse(dlnaFileMatch.Groups["id"].Value) : 0;
            return (sceneId, fileId);
        }

        // DeoVR: <endpoint>/api/dms/file/<fileId>/<sceneId> - <title>
        var deovrMatch = Regex.Match(pathAndQuery, @"api\/dms\/file\/(?<fileId>\d+)\/(?<sceneId>\d+) - .+");
        if (deovrMatch.Success)
        {
            var sceneId = int.Parse(deovrMatch.Groups["sceneId"].Value);
            var fileId = int.Parse(deovrMatch.Groups["fileId"].Value);
            return (sceneId, fileId);
        }

        // HereSphere: <endpoint>/api/dms/file/<fileId>
        var heresphereMatch = Regex.Match(pathAndQuery, @"api\/dms\/file\/(?<id>\d+)(?>\/?$|\?)");
        if (heresphereMatch.Success)
        {
            var fileId = int.Parse(heresphereMatch.Groups["id"].Value);
            return (null, fileId);
        }

        return (null, null);

        static async Task<bool> PointsToTheSameEndpoint(Uri resourceUri, Uri serverUri)
        {
            if (string.Equals(resourceUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
                return resourceUri.Port == serverUri.Port;

            if (!serverUri.IsLoopback)
                return false;

            var resourceHostAndPort = resourceUri.GetComponents(UriComponents.HostAndPort, UriFormat.Unescaped);
            if (!NetUtils.TryParseEndpoint(resourceHostAndPort, out var resourceEndpoint))
                return false;

            return await NetUtils.IsLocalAddressAsync(resourceEndpoint);
        }
    }

    private bool TryMatchLocal(SceneFile currentFile, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, ILocalScriptRepository localRepository)
    {
        if (LocalMatchType == XBVRLocalMatchType.None)
            return false;

        if (LocalMatchType == XBVRLocalMatchType.MatchToCurrentFile)
        {
            Logger.Trace("Trying to match scripts to current file using local repository [Source: {0}, Name: {1}]", currentFile.Filename, currentFile.Path);

            var searchResult = localRepository.SearchForScripts(currentFile.Filename, currentFile.Path, axes);
            result.Merge(searchResult);
        }

        return result.Count > 0;
    }

    private async Task<bool> TryMatchDms(SceneMetadata metadata, SceneFile currentFile, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, HttpClient client, CancellationToken token)
    {
        if (DmsMatchType == XBVRDmsMatchType.None)
            return false;

        var resultFiles = new Dictionary<DeviceAxis, SceneFile>();
        var scriptFiles = metadata.Files.Where(f => f.Type == "script");

        if (DmsMatchType == XBVRDmsMatchType.MatchToCurrentFile)
        {
            Logger.Trace("Trying to match scripts to current file using dms [Source: {0}, Name: {1}]", currentFile.Filename, currentFile.Path);

            foreach (var axis in axes)
                foreach (var matchedScript in DeviceAxisUtils.FindNamesMatchingAxis(axis, scriptFiles, s => s.Filename, currentFile.Filename))
                    AddToResult(resultFiles, axis, matchedScript);
        }
        else if (DmsMatchType == XBVRDmsMatchType.MatchSelectedOnly)
        {
            Logger.Trace("Trying to match selected scripts using dms");

            foreach (var scriptFile in scriptFiles.Where(f => f.IsSelected))
                foreach (var axis in DeviceAxisUtils.FindAxesMatchingName(axes, scriptFile.Filename))
                    AddToResult(resultFiles, axis, scriptFile);
        }

        foreach (var (axis, scriptFile) in resultFiles)
        {
            var scriptUri = new Uri(ServerBaseUri, $"/api/dms/file/{scriptFile.Id}");
            Logger.Trace("Downloading {0} script file [Uri: {1}]", axis, scriptUri);

            var scriptStream = await client.GetStreamAsync(scriptUri, token);
            var readerResult = FunscriptReader.Default.FromStream(scriptFile.Filename, scriptUri.ToString(), scriptStream);
            if (!readerResult.IsSuccess)
                continue;

            if (readerResult.IsMultiAxis)
                result.Merge(readerResult.Resources);
            else
                result[axis] = readerResult.Resource;
        }

        return result.Count > 0;

        static void AddToResult(IDictionary<DeviceAxis, SceneFile> result, DeviceAxis axis, SceneFile resource)
        {
            if (result.TryAdd(axis, resource))
                Logger.Debug("Matched {0} script to [Path: \"{1}\", Filename: \"{2}\"]", axis, resource?.Path, resource?.Filename);
            else
                Logger.Debug("Ignoring {0} script [Path: \"{1}\", Filename: \"{2}\"] because {0} is already matched to a script", axis, resource?.Path, resource?.Filename);
        }
    }

    private sealed record SceneMetadata([JsonProperty("file")] List<SceneFile> Files);
    private sealed record SceneFile(int Id, string Path, string Filename, string Type, [JsonProperty("is_selected_script")] bool IsSelected);
    private sealed record FileMetadata(int Id, [JsonProperty("scene_id")] int SceneId, string Type);
}

internal enum XBVRLocalMatchType
{
    [Description("Don't match scripts using local repository")]
    None,
    [Description("Match scripts based on currently playing XBVR file using local repository")]
    MatchToCurrentFile
}

internal enum XBVRDmsMatchType
{
    [Description("Don't match scripts using XBVR dms")]
    None,
    [Description("Match scripts based on currently playing XBVR file using XBVR dms")]
    MatchToCurrentFile,
    [Description("Match only scripts selected in XBVR using XBVR dms")]
    MatchSelectedOnly
}