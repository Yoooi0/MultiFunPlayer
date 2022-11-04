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
internal class UriToLocalMediaPathModifierViewModel : AbstractMediaPathModifier
{
    private readonly Dictionary<string, string> _mediaPathCache;
    private readonly Dictionary<long, FileInfo> _mediaSizeCache;
    private readonly HttpClient _client;

    [DependsOn(nameof(UriEndpoint))]
    public override string Description => UriEndpoint?.ToString();

    [JsonProperty] public DirectoryInfo MediaDirectory { get; set; }
    [JsonProperty] public EndPoint UriEndpoint { get; set; }

    public UriToLocalMediaPathModifierViewModel()
    {
        _mediaPathCache = new Dictionary<string, string>();
        _mediaSizeCache = new Dictionary<long, FileInfo>();
        _client = NetUtils.CreateHttpClient();
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

            var uriEndpoint = uri.HostNameType switch
            {
                UriHostNameType.IPv4 or UriHostNameType.IPv6 when IPAddress.TryParse(uri.Host, out var ipAddress) => new IPEndPoint(ipAddress, uri.Port),
                UriHostNameType.Dns => new DnsEndPoint(uri.Host, uri.Port),
                UriHostNameType.Basic when !string.IsNullOrWhiteSpace(uri.Host) => new DnsEndPoint(uri.Host, uri.Port),
                _ => default(EndPoint)
            };

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

            if (!localMediaFile.AsRefreshed().Exists)
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
}
