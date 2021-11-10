using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System.IO;
using System.Net;
using MultiFunPlayer.Common;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Net.Http;

namespace MultiFunPlayer.VideoSource.MediaResource.Modifier.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class UriToLocalMediaPathModifierViewModel : PropertyChangedBase, IMediaPathModifier
{
    private readonly Dictionary<string, string> _videoPathCache;
    private readonly Dictionary<long, FileInfo> _videoSizeCache;
    private readonly HttpClient _client;

    public string Name => "Uri To Local";

    [DependsOn(nameof(UriEndpoint))]
    public string Description => UriEndpoint?.ToString();

    [JsonProperty] public DirectoryInfo VideoDirectory { get; set; }
    [JsonProperty] public IPEndPoint UriEndpoint { get; set; }

    public UriToLocalMediaPathModifierViewModel()
    {
        _videoPathCache = new Dictionary<string, string>();
        _videoSizeCache = new Dictionary<long, FileInfo>();
        _client = WebUtils.CreateClient();
    }

    public void OnVideoDirectoryChanged()
    {
        _videoSizeCache.Clear();

        if (VideoDirectory == null)
            return;

        foreach (var (length, files) in VideoDirectory.SafeEnumerateFiles("*", SearchOption.AllDirectories).GroupBy(f => f.Length))
            if (files.Count() == 1)
                _videoSizeCache[length] = files.First();
    }

    public bool Process(ref string path)
    {
        if (VideoDirectory == null || UriEndpoint == null)
            return false;

        try
        {
            if (_videoPathCache.TryGetValue(path, out var cachedPath))
            {
                path = cachedPath;
                return true;
            }

            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)
             && !Uri.TryCreate($"http://{path}", UriKind.Absolute, out uri)
             && !Uri.TryCreate($"file://{path}", UriKind.Absolute, out uri))
                return false;

            var uriEndpoint = FindEndpoint(uri);
            if (!UriEndpoint.Equals(uriEndpoint))
                return false;

            var request = new HttpRequestMessage(HttpMethod.Head, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 6.1; Win64; x64)");

            using var response = _client.Send(request);
            var remoteLength = response.Content.Headers.ContentLength ?? 0;
            if (remoteLength == 0)
                return false;

            if (!_videoSizeCache.TryGetValue(remoteLength, out var localVideoFile) || localVideoFile == null)
                return false;

            if (localVideoFile.AsRefreshed().Exists == false)
            {
                _videoSizeCache.Remove(remoteLength);
                return false;
            }

            _videoPathCache[path] = localVideoFile.FullName;
            path = localVideoFile.FullName;
            return true;
        }
        catch { }

        return false;
    }

    public void SelectVideoDirectory()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        VideoDirectory = new DirectoryInfo(dialog.FileName);
    }

    private IPEndPoint FindEndpoint(Uri uri)
    {
        if (IPEndPoint.TryParse(uri.Authority, out var endpoint))
            return endpoint;

        try
        {
            var hostEntry = Dns.GetHostEntry(uri.Host);
            return new IPEndPoint(hostEntry.AddressList.First(), uri.Port);
        }
        catch { }

        return null;
    }
}
