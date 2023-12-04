using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Script.Repository;

[DisplayName("Stash")]
[JsonObject(MemberSerialization.OptIn)]
internal sealed class StashScriptRepository : AbstractScriptRepository
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
        _ = TryMatchFileSystem();
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

        bool TryMatchFileSystem()
        {
            foreach (var file in queryRespone.Data.FindScene.Files)
            {
                var directory = Path.GetDirectoryName(file.Path);
                var fileName = Path.GetFileName(file.Path);

                Logger.Debug("Trying to match scripts for video file [Directory: {0}, Filename: {1}]", directory, fileName);
                var searchResult = matcher.SearchForScripts(fileName, directory, axes);
                foreach(var (axis, resource) in searchResult)
                {
                    if (result.TryGetValue(axis, out var existingResource))
                        Logger.Debug("Overwriting already matched script [From: {0}, To: {1}]", existingResource.Name, resource.Name);

                    result[axis] = resource;
                }
            }

            return result.Count > 0;
        }
    }

    private sealed record QueryResponse(QueryData Data);
    private sealed record QueryData(QueryFindScene FindScene);
    private sealed record QueryFindScene(List<QueryFile> Files);
    private sealed record QueryFile(string Path);
}