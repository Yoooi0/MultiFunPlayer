using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Net.Http;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Jellyfin")]
internal sealed class JellyfinMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private CancellationTokenSource _refreshCancellationSource = new();
    private JellyfinSession _currentSession;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public Uri ServerBaseUri { get; set; } = new Uri("http://127.0.0.1:8096");
    public string ApiKey { get; set; }
    public JellyfinDevice SelectedDevice { get; set; } = null;
    public string SelectedDeviceId { get; set; }
    public ObservableConcurrentCollection<JellyfinDevice> Devices { get; set; } = [];

    public bool CanChangeDevice => IsDisconnected && !IsRefreshBusy && !string.IsNullOrEmpty(ApiKey) && Devices.Count != 0;
    public void OnSelectedDeviceChanged()
    {
        SelectedDeviceId = SelectedDevice?.Id;
        if (SelectedDeviceId == null)
            return;

        Logger.Debug("Selected {0}", SelectedDevice);
    }

    protected override void OnInitialActivate()
    {
        base.OnInitialActivate();
        _ = RefreshDevices();
    }

    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Name, ServerBaseUri, connectionType);

        if (ServerBaseUri == null)
            throw new MediaSourceException("Endpoint cannot be null");
        if (string.IsNullOrEmpty(ApiKey))
            throw new MediaSourceException("Api key cannot be empty");

        if (SelectedDeviceId == null)
            return false;
        if (SelectedDevice == null)
            await RefreshDevices();

        return SelectedDevice != null;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        using var client = NetUtils.CreateHttpClient();

        try
        {
            client.Timeout = TimeSpan.FromMilliseconds(1000);

            var uri = new Uri(ServerBaseUri, "/System/Ping");
            var response = await client.GetAsync(uri, token);
            response.EnsureSuccessStatusCode();

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0} at \"{1}\"", Name, ServerBaseUri);
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
            var task = await Task.WhenAny(ReadAsync(client, cancellationSource.Token), WriteAsync(client, cancellationSource.Token));
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

    private async Task ReadAsync(HttpClient client, CancellationToken token)
    {
        try
        {
            var lastState = default(PlayState);
            var lastItem = default(PlayItem);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested)
            {
                if (SelectedDeviceId == null)
                    continue;

                var sessionsUri = new Uri(ServerBaseUri, $"/Sessions?ApiKey={ApiKey}&DeviceId={SelectedDeviceId}");
                var response = await client.GetAsync(sessionsUri, token);
                response.EnsureSuccessStatusCode();

                var message = await response.Content.ReadAsStringAsync(token);
                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);

                try
                {
                    var o = JArray.Parse(message).Children<JObject>().FirstOrDefault();
                    _currentSession = o?.ToObject<JellyfinSession>();
                }
                catch (JsonException)
                {
                    continue;
                }

                var state = _currentSession?.State;
                var item = _currentSession?.Item;

                if (item == null && lastItem != null)
                {
                    PublishMessage(new MediaPathChangedMessage(null));
                    PublishMessage(new MediaPlayingChangedMessage(false));
                }

                if (item?.Path != null)
                {
                    if (lastItem?.Path == null || !string.Equals(lastItem.Path, item.Path, StringComparison.Ordinal))
                    {
                        PublishMessage(new MediaPathChangedMessage(item.Path));
                        PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromMilliseconds(item.RunTimeTicks / 10000.0)));
                        PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromMilliseconds(state.PositionTicks / 10000.0), ForceSeek: true));
                        PublishMessage(new MediaPlayingChangedMessage(!state.IsPaused));
                    }
                    else
                    {
                        if (lastItem == null || lastItem.RunTimeTicks != item.RunTimeTicks)
                            PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromMilliseconds(item.RunTimeTicks / 10000.0)));
                        if (lastState == null || lastState.IsPaused != state.IsPaused)
                            PublishMessage(new MediaPlayingChangedMessage(!state.IsPaused));
                        if (lastState == null || lastState.PositionTicks != state.PositionTicks)
                            PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromMilliseconds(state.PositionTicks / 10000.0)));
                    }
                }

                lastState = state;
                lastItem = item;
            }
        }
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException t) { t.Throw(); }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(HttpClient client, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await WaitForMessageAsync(token);
                var message = await ReadMessageAsync(token);

                if (_currentSession == null)
                    continue;

                var baseUri = new Uri(ServerBaseUri, $"/Sessions/{_currentSession.Id}/Playing/");
                var uri = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => new Uri(baseUri, $"{(playPauseMessage.ShouldBePlaying ? "Unpause" : "Pause")}?ApiKey={ApiKey}"),
                    MediaSeekMessage seekMessage => new Uri(baseUri, $"Seek?ApiKey={ApiKey}&SeekPositionTicks={(long)(seekMessage.Position.TotalMilliseconds * 10000)}"),
                    _ => null
                };

                if (uri == null)
                    continue;

                Logger.Info("Sending \"{0}\" to \"{1}\"", uri, Name);
                _ = await client.PostAsync(uri, null, token);
            }
        }
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException t) { t.Throw(); }
        catch (OperationCanceledException) { }
    }

    public bool CanRefreshDevices => !IsRefreshBusy && IsDisconnected && ServerBaseUri != null && !string.IsNullOrEmpty(ApiKey);
    public bool IsRefreshBusy { get; set; }

    private int _isRefreshingFlag;
    public async Task RefreshDevices()
    {
        if (ServerBaseUri == null)
            return;
        if (string.IsNullOrEmpty(ApiKey))
            return;

        if (Interlocked.CompareExchange(ref _isRefreshingFlag, 1, 0) != 0)
            return;

        try
        {
            var token = _refreshCancellationSource.Token;
            token.ThrowIfCancellationRequested();

            IsRefreshBusy = true;
            await DoRefreshDevices(token);
        }
        catch (Exception e)
        {
            Logger.Warn(e, $"{Name} device refresh failed with exception");
        }
        finally
        {
            Interlocked.Decrement(ref _isRefreshingFlag);
            IsRefreshBusy = false;
        }

        async Task DoRefreshDevices(CancellationToken token)
        {
            await Task.Delay(250, token);
            Logger.Debug("Refreshing devices");

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(5000);

            var uri = new Uri(ServerBaseUri, $"/Devices?ApiKey={ApiKey}");
            var response = await client.GetAsync(uri, token);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(token);
            Logger.Trace(() => $"Received \"{content}\" from \"{Name}\"");

            var o = JObject.Parse(content);
            foreach (var device in o["Items"].OfType<JObject>())
            {
                var customName = await TryGetCustomName(device);
                if (customName != null)
                    device["Name"] = customName;
            }

            var currentDevices = o["Items"].ToObject<List<JellyfinDevice>>();
            var lastSelectedMachineIdentifier = SelectedDeviceId;
            Devices.RemoveRange(Devices.Except(currentDevices).ToList());
            Devices.AddRange(currentDevices.Except(Devices).ToList());

            SelectDeviceById(lastSelectedMachineIdentifier);

            await Task.Delay(250, token);

            async ValueTask<string> TryGetCustomName(JObject device)
            {
                if (device.TryGetValue<string>("CustomName", out var customName) && !string.IsNullOrWhiteSpace(customName))
                    return customName;

                if (!device.TryGetValue<string>("Id", out var id) || string.IsNullOrWhiteSpace(id))
                    return null;

                try
                {
                    uri = new Uri(ServerBaseUri, $"/Devices/Options?ApiKey={ApiKey}&id={id}");
                    response = await client.GetAsync(uri, token);
                    response.EnsureSuccessStatusCode();

                    content = await response.Content.ReadAsStringAsync(token);
                    Logger.Trace(() => $"Received \"{content}\" from \"{Name}\"");

                    var options = JObject.Parse(content);
                    if (options.TryGetValue<string>("CustomName", out customName) && !string.IsNullOrWhiteSpace(customName))
                        return customName;
                }
                catch (Exception e)
                {
                    Logger.Warn(e, $"Failed to read custom name for device \"{id}\"");
                }

                return null;
            }
        }
    }

    private void SelectDeviceById(string deviceId)
    {
        SelectedDevice = Devices.FirstOrDefault(p => string.Equals(p.Id, deviceId, StringComparison.Ordinal));
        if (SelectedDevice == null)
            SelectedDeviceId = deviceId;
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(ServerBaseUri)] = ServerBaseUri?.ToString();
            settings[nameof(SelectedDevice)] = SelectedDeviceId;

            settings[nameof(ApiKey)] = ProtectedStringUtils.Protect(ApiKey,
                e => Logger.Warn(e, "Failed to encrypt \"{0}\"", nameof(ApiKey)));
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedDevice), out var deviceId))
                SelectDeviceById(deviceId);
            if (settings.TryGetValue<Uri>(nameof(ServerBaseUri), out var serverBaseUri))
                ServerBaseUri = serverBaseUri;

            if (settings.TryGetValue<string>(nameof(ApiKey), out var encryptedApiKey))
                ApiKey = ProtectedStringUtils.Unprotect(encryptedApiKey,
                    e => Logger.Warn(e, "Failed to decrypt \"{0}\"", nameof(ApiKey)));
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ServerBaseUri
        s.RegisterAction<string>($"{Name}::ServerBaseUri::Set", s => s.WithLabel("Endpoint").WithDescription("scheme://ipOrHost:port"), serverBaseUri =>
        {
            if (Uri.TryCreate(serverBaseUri, UriKind.Absolute, out var uri))
                ServerBaseUri = uri;
        });
        #endregion

        #region ApiKey
        s.RegisterAction<string>($"{Name}::ApiKey::Set", s => s.WithLabel("Api key"), apiKey => ApiKey = apiKey);
        #endregion

        #region SelectedDevice
        s.RegisterAction<string>($"{Name}::Device::SetByName", s => s.WithLabel("Name"), name => {
            var devcie = Devices.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (devcie == null)
                return;

            SelectDeviceById(devcie.Id);
        });
        #endregion

        #region RefreshDevices
        s.RegisterAction($"{Name}::RefreshDevices", async () => { if (CanRefreshDevices) await RefreshDevices(); });
        #endregion
    }

    protected override void Dispose(bool disposing)
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        base.Dispose(disposing);
    }

    internal sealed record JellyfinDevice(string Name, string Id, string AppName, string AppVersion)
    {
        public bool Equals(JellyfinDevice other) => string.Equals(Id, other?.Id, StringComparison.Ordinal);
        public override int GetHashCode() => Id.GetHashCode();
    }

    private sealed record JellyfinSession(string Id, [property: JsonProperty("PlayState")] PlayState State, [property: JsonProperty("NowPlayingItem")] PlayItem Item);
    private sealed record PlayState(long PositionTicks, bool IsPaused);
    private sealed record PlayItem(long RunTimeTicks, string Path);
}
