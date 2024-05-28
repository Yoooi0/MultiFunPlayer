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
    [JsonProperty] public string ApiKey { get; set; } = null;
    [JsonProperty] public StashLocalMatchType LocalMatchType { get; set; } = StashLocalMatchType.MatchToCurrentFile;
    [JsonProperty] public StashDmsMatchType DmsMatchType { get; set; } = StashDmsMatchType.MatchToAxis;
    [JsonProperty] public DeviceAxis DmsMatchAxis { get; set; } = DeviceAxis.All.FirstOrDefault();

    public override async ValueTask<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(
        MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, ILocalScriptRepository localRepository, CancellationToken token)
    {
        if (ServerBaseUri == null)
            return [];

        Logger.Trace("Searching for scene id in \"{0}\"", mediaResource.Path);
        var sceneId = await TryGetSceneId(mediaResource);

        Logger.Debug("Found scene id [Id: {0}]", sceneId);
        if (string.IsNullOrWhiteSpace(sceneId))
            return [];

        using var client = NetUtils.CreateHttpClient();
        if (!string.IsNullOrWhiteSpace(ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("ApiKey", ApiKey);

        var result = new Dictionary<DeviceAxis, IScriptResource>();

        var query = $"{{\"query\":\"{{ findScene(id: {sceneId}) {{ id, title, files {{ path }}, paths {{ funscript }} }} }}\",\"variables\":null}}";
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(ServerBaseUri, "/graphql"),
            Method = HttpMethod.Post,
            Content = new StringContent(query, Encoding.UTF8, "application/json")
        };

        Logger.Trace("Sending query \"{0}\"", query);
        var response = await client.SendAsync(request, token);
        var content = await response.Content.ReadAsStringAsync(token);

        Logger.Trace("Received query response \"{0}\"", content);

        var queryRespone = JsonConvert.DeserializeObject<QueryResponse>(content);
        var primaryFile = queryRespone?.Data?.FindScene?.Files?.FirstOrDefault();
        if (!TryMatchLocal(primaryFile, axes, result, localRepository))
            await TryMatchDms(queryRespone, result, client, token);

        return result;
    }

    private async ValueTask<string> TryGetSceneId(MediaResourceInfo mediaResource)
    {
        if (!mediaResource.IsUrl)
            return null;

        var mediaResourceUri = new Uri(mediaResource.Path);
        if (!await PointsToTheSameEndpoint(mediaResourceUri, ServerBaseUri))
        {
            Logger.Debug("Ignoring \"{0}\" resource because it does not point to \"{1}\"", mediaResource.Path, ServerBaseUri);
            return null;
        }

        var pathAndQuery = mediaResourceUri.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);

        // DLNA: <endpoint>/res?scene=<sceneId>
        var match = Regex.Match(pathAndQuery, @"res\?.*scene=(?<id>\d+?)(?>$|&)");
        if (match.Success)
            return match.Groups["id"].Value;

        // Stream: <endpoint>/scene/<sceneId>/stream
        match = Regex.Match(pathAndQuery, @"scene\/(?<id>\d+)\/stream");
        if (match.Success)
            return match.Groups["id"].Value;

        return null;

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

    private bool TryMatchLocal(QueryFile primaryFile, IEnumerable<DeviceAxis> axes, Dictionary<DeviceAxis, IScriptResource> result, ILocalScriptRepository localRepository)
    {
        if (primaryFile == null)
            return false;
        if (LocalMatchType == StashLocalMatchType.None)
            return false;

        if (LocalMatchType == StashLocalMatchType.MatchToCurrentFile)
        {
            var source = Path.GetDirectoryName(primaryFile.Path);
            var name = Path.GetFileName(primaryFile.Path);

            Logger.Trace("Trying to match scripts to primary file using local repository [Source: {0}, Name: {1}]", source, name);

            var searchResult = localRepository.SearchForScripts(name, source, axes);
            result.Merge(searchResult);
        }

        return result.Count > 0;
    }

    private async Task<bool> TryMatchDms(QueryResponse queryRespone, Dictionary<DeviceAxis, IScriptResource> result, HttpClient client, CancellationToken token)
    {
        if (DmsMatchType == StashDmsMatchType.None)
            return false;
        if (queryRespone?.Data?.FindScene?.Paths?.Funscript == null)
            return false;

        var scriptUri = new Uri(queryRespone.Data.FindScene.Paths.Funscript);

        var scriptName = queryRespone.Data.FindScene.Id;
        if (!string.IsNullOrWhiteSpace(queryRespone.Data.FindScene.Title))
            scriptName += $" - {queryRespone.Data.FindScene.Title}";
        scriptName += ".funscript";

        Logger.Trace("Downloading scene script file [Uri: {0}]", scriptUri);

        var scriptStream = await client.GetStreamAsync(scriptUri, token);
        var readerResult = FunscriptReader.Default.FromStream(scriptName, ServerBaseUri.ToString(), scriptStream);
        if (!readerResult.IsSuccess)
            return false;

        if (readerResult.IsMultiAxis)
        {
            Logger.Trace("Matching scene multi-axis script");
            result.Merge(readerResult.Resources);
        }
        else if (DmsMatchAxis != null)
        {
            Logger.Trace("Matching scene script to {0}", DmsMatchAxis);
            result[DmsMatchAxis] = readerResult.Resource;
        }

        return result.Count > 0;
    }

    private sealed record QueryResponse(QueryData Data);
    private sealed record QueryData(QueryFindScene FindScene);
    private sealed record QueryFindScene(string Id, string Title, List<QueryFile> Files, QueryPaths Paths);
    private sealed record QueryFile(string Path);
    private sealed record QueryPaths(string Funscript);
}

internal enum StashLocalMatchType
{
    [Description("Don't match scripts using local repository")]
    None,
    [Description("Match scripts based on currently playing Stash file using local repository")]
    MatchToCurrentFile
}

internal enum StashDmsMatchType
{
    [Description("Don't match scripts using Stash dms")]
    None,
    [Description("Match scene script to selected axis using Stash dms")]
    MatchToAxis
}