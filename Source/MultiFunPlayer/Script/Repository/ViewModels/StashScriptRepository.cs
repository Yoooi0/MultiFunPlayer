using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using NLog;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Script.Repository.ViewModels;

[DisplayName("Stash")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class StashScriptRepository : AbstractScriptRepository
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonProperty] public Uri ServerBaseUri { get; set; } = new Uri("http://127.0.0.1:9999");
    [JsonProperty] public StashVideoMatchType VideoMatchType { get; set; } = StashVideoMatchType.UseFirstMatchOnly;
    [JsonProperty] public DeviceAxis ScriptMatchAxis { get; set; } = DeviceAxis.All.FirstOrDefault();

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
    {
        if (ServerBaseUri == null)
            return [];

        if (!TryGetSceneId(mediaResource, out var sceneId))
            return [];

        Logger.Debug("Found Stash scene id [Id: {0}]", sceneId);

        using var client = NetUtils.CreateHttpClient();
        var result = new Dictionary<DeviceAxis, IScriptResource>();

        var query = $"{{\"query\":\"{{ findScene(id: {sceneId}) {{ id, title, files {{ path }}, paths {{ funscript }} }} }}\",\"variables\":null}}";
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(ServerBaseUri, "/graphql"),
            Method = HttpMethod.Post,
            Content = new StringContent(query, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, token);
        var content = await response.Content.ReadAsStringAsync(token);

        Logger.Trace("Received Stash api response \"{0}\"", response);

        var queryRespone = JsonConvert.DeserializeObject<QueryResponse>(content);
        _ = TryMatchLocal(queryRespone, axes, result, localRepository) || await TryMatchDms(queryRespone, result, client, token);
        return result;
    }

    private bool TryGetSceneId(MediaResourceInfo mediaResource, out int sceneId)
    {
        sceneId = -1;
        if (mediaResource.IsFile)
        {
            var match = Regex.Match(mediaResource.Name, @"^(?<id>\d+) - .+");
            if (!match.Success)
                return false;

            sceneId = int.Parse(match.Groups["id"].Value);
            return true;
        }
        else if (mediaResource.IsUrl)
        {
            var mediaResourceUri = new Uri(mediaResource.Path);
            if (!string.Equals(mediaResourceUri.Host, ServerBaseUri.Host, StringComparison.OrdinalIgnoreCase))
                return false;

            var pathAndQuery = mediaResourceUri.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);

            // <endpoint>/res?scene=<sceneId>
            var match = Regex.Match(pathAndQuery, @"scene=(?<id>\d+?)");
            if (match.Success)
            {
                sceneId = int.Parse(match.Groups["id"].Value);
                return true;
            }

            // <endpoint>/scene/<sceneId>/stream
            match = Regex.Match(pathAndQuery, @"scene\/(?<id>\d+)\/stream");
            if (match.Success)
            {
                sceneId = int.Parse(match.Groups["id"].Value);
                return true;
            }
        }

        return false;
    }

    private bool TryMatchLocal(QueryResponse queryRespone, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, ILocalScriptRepository localRepository)
    {
        if (queryRespone?.Data?.FindScene?.Files == null)
            return false;

        foreach (var file in queryRespone.Data.FindScene.Files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            var fileName = Path.GetFileName(file.Path);

            Logger.Debug("Trying to match scripts for video file [Directory: {0}, Filename: {1}]", directory, fileName);
            var searchResult = localRepository.SearchForScripts(fileName, directory, axes);
            if (searchResult.Count == 0)
                continue;

            if (VideoMatchType == StashVideoMatchType.UseFirstMatchOnly)
            {
                result.Merge(searchResult);
                return true;
            }

            foreach (var (axis, resource) in searchResult)
            {
                if (VideoMatchType == StashVideoMatchType.MatchAllOverwrite && result.TryGetValue(axis, out var existingResource))
                    Logger.Debug("Overwriting already matched script [From: {0}, To: {1}]", existingResource.Name, resource.Name);

                var validMatch = (VideoMatchType == StashVideoMatchType.MatchAllUseFirst && !result.ContainsKey(axis))
                              || (VideoMatchType == StashVideoMatchType.MatchAllOverwrite);

                if (validMatch)
                {
                    Logger.Debug("Matched \"{0}\" to {1} axis", resource.Name, axis);
                    result[axis] = resource;
                }
            }
        }

        return result.Count > 0;
    }

    private async Task<bool> TryMatchDms(QueryResponse queryRespone, Dictionary<DeviceAxis, IScriptResource> result, HttpClient client, CancellationToken token)
    {
        if (queryRespone?.Data?.FindScene?.Paths?.Funscript == null)
            return false;

        var scriptUri = queryRespone.Data.FindScene.Paths.Funscript;

        var scriptName = queryRespone.Data.FindScene.Id;
        if (!string.IsNullOrWhiteSpace(queryRespone.Data.FindScene.Title))
            scriptName += $" - {queryRespone.Data.FindScene.Title}";
        scriptName += ".funscript";

        Logger.Trace("Downloading script file [Uri: {0}]", scriptUri);

        var scriptStream = await client.GetStreamAsync(scriptUri, token);
        var readerResult = FunscriptReader.Default.FromStream(scriptName, ServerBaseUri.ToString(), scriptStream);
        if (!readerResult.IsSuccess)
            return false;

        if (readerResult.IsMultiAxis)
            result.Merge(readerResult.Resources);
        else if (ScriptMatchAxis != null)
            result[ScriptMatchAxis] = readerResult.Resource;

        return result.Count > 0;
    }

    private sealed record QueryResponse(QueryData Data);
    private sealed record QueryData(QueryFindScene FindScene);
    private sealed record QueryFindScene(string Id, string Title, List<QueryFile> Files, QueryPaths Paths);
    private sealed record QueryFile(string Path);
    private sealed record QueryPaths(string Funscript);
}

internal enum StashVideoMatchType
{
    [Description("Use first video that matches at least one axis")]
    UseFirstMatchOnly,
    [Description("Try to match all videos, for each axis use first matched script")]
    MatchAllUseFirst,
    [Description("Try to match all videos, overwrite if multiple scripts match an axis")]
    MatchAllOverwrite
}