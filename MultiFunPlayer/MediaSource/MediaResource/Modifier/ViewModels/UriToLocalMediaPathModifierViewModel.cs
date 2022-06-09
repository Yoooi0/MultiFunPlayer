using Newtonsoft.Json;
using PropertyChanged;
using System.IO;
using System.Net;
using MultiFunPlayer.Common;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Net.Http;
using System.ComponentModel;

namespace MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels;

[DisplayName("Uri To Local")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class UriToLocalMediaPathModifierViewModel : AbstractMediaPathModifier
{
    private readonly Dictionary<string, string> _mediaPathCache;
    private readonly Dictionary<long, FileInfo> _mediaSizeCache;
    private readonly HttpClient _client;

    [DependsOn(nameof(UriEndpoint))]
    public override string Description => UriEndpoint?.ToString();

    [JsonProperty] public DirectoryInfo MediaDirectory { get; set; }
    [JsonProperty] public IPEndPoint UriEndpoint { get; set; }

    public UriToLocalMediaPathModifierViewModel()
    {
        _mediaPathCache = new Dictionary<string, string>();
        _mediaSizeCache = new Dictionary<long, FileInfo>();
        _client = WebUtils.CreateClient();
    }

    public void OnMediaDirectoryChanged()
    {
        _mediaSizeCache.Clear();

        if (MediaDirectory == null)
            return;

        foreach (var (length, files) in MediaDirectory.SafeEnumerateFiles("*.*", IOUtils.CreateEnumerationOptions(true)).GroupBy(f => f.Length))
            if (files.Count() == 1)
                _mediaSizeCache[length] = files.First();
    }

    public override bool Process(ref string path)
    {
        if (MediaDirectory == null || UriEndpoint == null)
            return false;

        try
        {
            if (_mediaPathCache.TryGetValue(path, out var cachedPath))
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

            if (!_mediaSizeCache.TryGetValue(remoteLength, out var localMediaFile) || localMediaFile == null)
                return false;

            if (localMediaFile.AsRefreshed().Exists == false)
            {
                _mediaSizeCache.Remove(remoteLength);
                return false;
            }

            _mediaPathCache[path] = localMediaFile.FullName;
            path = localMediaFile.FullName;
            return true;
        }
        catch { }

        return false;
    }

    public void SelectMediaDirectory()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        MediaDirectory = new DirectoryInfo(dialog.FileName);
    }

    private IPEndPoint FindEndpoint(Uri uri)
    {
        if (uri.IsFile)
            return null;

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
