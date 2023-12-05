using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using NLog;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Script.Repository.ViewModels;

[DisplayName("Stash")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class StashScriptRepository : AbstractScriptRepository
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonProperty] public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 9999);
    [JsonProperty] public StashVideoMatchType VideoMatchType { get; set; } = StashVideoMatchType.UseFirstMatchOnly;

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
    {
        if (Endpoint == null)
            return [];

        if (!TryGetSceneId(out var sceneId))
            return [];

        using var client = NetUtils.CreateHttpClient();
        var result = new Dictionary<DeviceAxis, IScriptResource>();

        var query = $"{{\"query\":\"{{ findScene(id: {sceneId}) {{ files {{ path }} }} }}\",\"variables\":null}}";
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri($"http://{Endpoint.ToUriString()}/graphql"),
            Method = HttpMethod.Post,
            Content = new StringContent(query, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, token);
        var content = await response.Content.ReadAsStringAsync(token);

        var queryRespone = JsonConvert.DeserializeObject<QueryResponse>(content);
        _ = TryMatchLocal(queryRespone, axes, result, localRepository) || await TryMatchDms(queryRespone, result, client, token);
        return result;

        bool TryGetSceneId(out int sceneId)
        {
            sceneId = -1;
            if (mediaResource.IsPath)
            {
                var match = Regex.Match(mediaResource.Name, @"^(?<id>\d+) - .+");
                if (!match.Success)
                    return false;

                sceneId = int.Parse(match.Groups["id"].Value);
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
                var match = Regex.Match(mediaResourceUri.Query, @"scene=(?<id>\d+?)");
                if (match.Success)
                {
                    sceneId = int.Parse(match.Groups["id"].Value);
                    return true;
                }

                // <endpoint>/scene/<sceneId>/stream
                match = Regex.Match(mediaResourceUri.Query, @"scene\/(?<id>\d+)\/stream");
                if (match.Success)
                {
                    sceneId = int.Parse(match.Groups["id"].Value);
                    return true;
                }
            }

            return false;
        }
    }

    private bool TryMatchLocal(QueryResponse queryRespone, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, ILocalScriptRepository localRepository)
    {
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
                result = searchResult;
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

    private static async Task<bool> TryMatchDms(QueryResponse queryRespone, Dictionary<DeviceAxis, IScriptResource> result, HttpClient client, CancellationToken token)
    {
        if (!DeviceAxis.TryParse("L0", out var axis))
            return false;

        var scriptUri = queryRespone.Data.FindScene.Paths.Funscript;
        var scriptName = Path.ChangeExtension(queryRespone.Data.FindScene.Files[0].Path, ".funscript");
        Logger.Trace("Downloading {0} script file [Uri: {1}]", axis, scriptUri);

        var scriptStream = await client.GetStreamAsync(scriptUri, token);
        var readerResult = FunscriptReader.Default.FromStream(scriptName, scriptUri, scriptStream);
        if (!readerResult.IsSuccess)
            return false;

        if (readerResult.IsMultiAxis)
            result.Merge(readerResult.Resources);
        else
            result[axis] = readerResult.Resource;

        return result.Count > 0;
    }

    private sealed record QueryResponse(QueryData Data);
    private sealed record QueryData(QueryFindScene FindScene);
    private sealed record QueryFindScene(List<QueryFile> Files, QueryPaths Paths);
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