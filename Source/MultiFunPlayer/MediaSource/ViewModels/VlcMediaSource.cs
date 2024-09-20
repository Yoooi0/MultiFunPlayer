using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("VLC")]
internal sealed class VlcMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && !string.IsNullOrEmpty(Password);

    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);
    public string Password { get; set; } = null;

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Name, Endpoint?.ToUriString(), connectionType);

        if (Endpoint == null)
            throw new MediaSourceException("Endpoint cannot be null");

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        using var client = NetUtils.CreateHttpClient();
        var version = -1;

        try
        {
            client.Timeout = TimeSpan.FromMilliseconds(500);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{Password}")));

            var uri = new Uri($"http://{Endpoint.ToUriString()}/requests/status.json");
            var response = await client.GetAsync(uri, token);
            response.EnsureSuccessStatusCode();

            var message = await response.Content.ReadAsStringAsync(token);
            var document = JObject.Parse(message);

            if (!document.TryGetValue("apiversion", out version))
            {
                Logger.Trace("Unable to determine version from \"{0}\"", message);
                throw new MediaSourceException("Unable to determine VLC version");
            }

            if (version is not (3 or 4))
                throw new MediaSourceException($"Unsupported VLC version \"{version}\"");

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0} at \"{1}\"", Name, Endpoint?.ToUriString());
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(client, version, cancellationSource.Token), WriteAsync(client, version, cancellationSource.Token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        if (IsDisposing)
            return;

        PublishMessage(new MediaPathChangedMessage(null));
        PublishMessage(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(HttpClient client, int version, CancellationToken token)
    {
        var statusUri = new Uri($"http://{Endpoint.ToUriString()}/requests/status.json");
        var playlistUri = new Uri($"http://{Endpoint.ToUriString()}/requests/playlist.json");
        var playerState = new PlayerState();

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(200, token);

                var statusResponse = await client.GetAsync(statusUri, token);
                if (statusResponse == null)
                    continue;

                statusResponse.EnsureSuccessStatusCode();
                var statusMessage = await statusResponse.Content.ReadAsStringAsync(token);

                Logger.Trace("Received \"{0}\" from \"{1}\"", statusMessage, Name);
                var statusDocument = JObject.Parse(statusMessage);

                if (!statusDocument.TryGetValue<int>("currentplid", out var playlistId))
                    continue;

                bool ShouldResetState() => version switch
                {
                    3 when playlistId < 0 => true,
                    4 when statusDocument.TryGetValue<string>("state", out var state) && string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase) => true,
                    not (3 or 4) => throw new UnreachableException(),
                    _ => false
                };

                if (ShouldResetState())
                {
                    ResetState();
                    continue;
                }

                if (playlistId != playerState.PlaylistId)
                {
                    var playlistResponse = await client.GetAsync(playlistUri, token);
                    if (playlistResponse == null)
                        continue;

                    playlistResponse.EnsureSuccessStatusCode();
                    var playlistMessage = await playlistResponse.Content.ReadAsStringAsync(token);

                    Logger.Trace("Received \"{0}\" from \"{1}\"", playlistMessage, Name);

                    var playlistDocument = JToken.Parse(playlistMessage);
                    if (playlistDocument.SelectToken("$..[?(@.type == 'leaf' && @.current == 'current')]") is not JObject playlistItem)
                    {
                        ResetState();
                        continue;
                    }

                    if (!playlistItem.TryGetValue<string>("uri", out var path) || string.IsNullOrWhiteSpace(path))
                    {
                        ResetState();
                        continue;
                    }

                    if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                        PublishMessage(new MediaPathChangedMessage(uri.LocalPath));
                    else if (Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out uri))
                        PublishMessage(new MediaPathChangedMessage(uri.ToString()));
                    else
                        PublishMessage(new MediaPathChangedMessage(Uri.UnescapeDataString(path)));

                    playerState.PlaylistId = playlistId;
                }

                if (statusDocument.TryGetValue<string>("state", out var state) && state != playerState.State)
                {
                    PublishMessage(new MediaPlayingChangedMessage(string.Equals(state, "playing", StringComparison.OrdinalIgnoreCase)));
                    playerState.State = state;
                }

                if (statusDocument.TryGetValue<double>("length", out var duration))
                {
                    if (version == 3 && statusDocument.TryGetValue<double>("position", out var positionPercent) && statusDocument.TryGetValue<double>("time", out var time))
                        duration = Math.Round(Math.Max(Math.Max(playerState.Duration ?? -1, duration), time / positionPercent), 3);

                    if (duration >= 0 && duration != playerState.Duration)
                    {
                        PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                        playerState.Duration = duration;
                    }
                }

                if (statusDocument.TryGetValue<double>("rate", out var speed) && speed > 0 && speed != playerState.Speed)
                {
                    PublishMessage(new MediaSpeedChangedMessage(speed));
                    playerState.Speed = speed;
                }

                var position = version switch
                {
                    3 when statusDocument.TryGetValue<double>("position", out var positionPercent) && playerState.Duration != null => positionPercent * (double)playerState.Duration,
                    4 when statusDocument.TryGetValue<double>("time", out var time) => time,
                    not (3 or 4) => throw new UnreachableException(),
                    _ => -1
                };

                if (position >= 0 && position != playerState.Position)
                {
                    PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
                    playerState.Position = position;
                }
            }
        }
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException t) { t.Throw(); }
        catch (OperationCanceledException) { }

        void ResetState()
        {
            if (playerState.PlaylistId == null)
                return;

            playerState = new PlayerState();

            PublishMessage(new MediaPathChangedMessage(null));
            PublishMessage(new MediaPlayingChangedMessage(false));
        }
    }

    private async Task WriteAsync(HttpClient client, int version, CancellationToken token)
    {
        var statusUri = $"http://{Endpoint.ToUriString()}/requests/status.json";

        try
        {
            while (!token.IsCancellationRequested)
            {
                await WaitForMessageAsync(token);
                var message = await ReadMessageAsync(token);

                var uriArguments = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => playPauseMessage.ShouldBePlaying ? "pl_forceresume" : "pl_forcepause",
                    MediaSeekMessage seekMessage => $"seek&val={(int)seekMessage.Position.TotalSeconds}",
                    MediaChangePathMessage changePathMessage => string.IsNullOrWhiteSpace(changePathMessage.Path) ? "pl_stop" : $"in_play&input={Uri.EscapeDataString(changePathMessage.Path)}",
                    MediaChangeSpeedMessage changeSpeedMessage => $"rate&val={changeSpeedMessage.Speed.ToString("F4").Replace(',', '.')}",
                    _ => null
                };

                if (uriArguments == null)
                    continue;

                var requestUri = new Uri($"{statusUri}?command={uriArguments}");
                Logger.Trace("Sending \"{0}\" to \"{1}\"", uriArguments, Name);

                var response = await client.GetAsync(requestUri, token);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException t) { t.Throw(); }
        catch (OperationCanceledException) { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Endpoint)] = Endpoint?.ToUriString();
            settings[nameof(Password)] = ProtectedStringUtils.Protect(Password,
                e => Logger.Warn(e, "Failed to encrypt \"{0}\"", nameof(Password)));
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;

            if (settings.TryGetValue<string>(nameof(Password), out var encryptedPassword))
                Password = ProtectedStringUtils.Unprotect(encryptedPassword,
                    e => Logger.Warn(e, "Failed to decrypt \"{0}\"", nameof(Password)));
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Name}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ipOrHost:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        });
        #endregion
    }

    private sealed class PlayerState
    {
        public double? Position { get; set; }
        public double? Speed { get; set; }
        public string State { get; set; }
        public double? Duration { get; set; }
        public int? PlaylistId { get; set; }
    }
}
