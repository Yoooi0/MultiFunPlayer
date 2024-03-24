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

[DisplayName("Emby")]
internal sealed class EmbyMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private CancellationTokenSource _refreshCancellationSource = new();
    private EmbySession _currentSession;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public Uri ServerBaseUri { get; set; } = new Uri("http://127.0.0.1:8096");
    public string ApiKey { get; set; }
    public EmbyDevice SelectedDevice { get; set; } = null;
    public string SelectedDeviceId { get; set; }
    public ObservableConcurrentCollection<EmbyDevice> Devices { get; set; } = [];

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

    protected override async ValueTask<bool> OnConnectingAsync()
    {
        if (SelectedDeviceId == null)
            return false;
        if (SelectedDevice == null)
            await RefreshDevices();

        return SelectedDevice != null && await base.OnConnectingAsync();
    }

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, ServerBaseUri);
            if (ServerBaseUri == null)
                throw new Exception("Endpoint cannot be null.");
            if (string.IsNullOrEmpty(ApiKey))
                throw new Exception("Api key cannot be empty.");

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(1000);

            var uri = new Uri(ServerBaseUri, "/System/Ping");
            var response = await UnwrapTimeout(() => client.GetAsync(uri, token));
            response.EnsureSuccessStatusCode();

            Status = ConnectionStatus.Connected;
            ClearPendingMessages();

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
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(500, token);
                if (SelectedDeviceId == null)
                    continue;

                var sessionsUri = new Uri(ServerBaseUri, $"/Sessions?api_key={ApiKey}&DeviceId={SelectedDeviceId}");
                var response = await UnwrapTimeout(() => client.GetAsync(sessionsUri, token));
                if (response == null)
                    continue;

                response.EnsureSuccessStatusCode();
                var message = await response.Content.ReadAsStringAsync(token);

                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);
                try
                {
                    var o = JArray.Parse(message).Children<JObject>().FirstOrDefault();
                    _currentSession = o?.ToObject<EmbySession>();
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
                        PublishMessage(new MediaSpeedChangedMessage(state.PlaybackRate));
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
                        if (lastState == null || double.Abs(lastState.PlaybackRate - state.PlaybackRate) > 0.00001)
                            PublishMessage(new MediaSpeedChangedMessage(state.PlaybackRate));
                    }
                }

                lastState = state;
                lastItem = item;
            }
        }
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
                    MediaPlayPauseMessage playPauseMessage => new Uri(baseUri, $"{(playPauseMessage.ShouldBePlaying ? "Unpause" : "Pause")}?api_key={ApiKey}"),
                    MediaSeekMessage seekMessage => new Uri(baseUri, $"Seek?api_key={ApiKey}&SeekPositionTicks={(long)(seekMessage.Position.TotalMilliseconds * 10000)}"),
                    _ => null
                };

                if (uri == null)
                    continue;

                Logger.Info("Sending \"{0}\" to \"{1}\"", uri, Name);
                _ = await client.PostAsync(uri, null, token);
            }
        }
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

            var uri = new Uri(ServerBaseUri, $"/Devices?api_key={ApiKey}");
            var response = await UnwrapTimeout(() => client.GetAsync(uri, token));
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(token);
            Logger.Trace(() => $"Received \"{content}\" from \"{Name}\"");

            var o = JObject.Parse(content);
            foreach (var device in o["Items"].OfType<JObject>())
            {
                if (!device.TryGetValue<string>("Id", out var id) || string.IsNullOrWhiteSpace(id))
                    continue;

                try
                {
                    uri = new Uri(ServerBaseUri, $"/Devices/Options?api_key={ApiKey}&Id={id}");
                    response = await UnwrapTimeout(() => client.GetAsync(uri, token));
                    response.EnsureSuccessStatusCode();

                    content = await response.Content.ReadAsStringAsync(token);
                    Logger.Trace(() => $"Received \"{content}\" from \"{Name}\"");

                    var options = JObject.Parse(content);
                    if (options.TryGetValue<string>("CustomName", out var customName) && !string.IsNullOrWhiteSpace(customName))
                        device["Name"] = customName;
                }
                catch (Exception e)
                {
                    Logger.Warn(e, $"Failed to read custom name for device \"{id}\"");
                }
            }

            var currentDevices = o["Items"].ToObject<List<EmbyDevice>>();
            var lastSelectedMachineIdentifier = SelectedDeviceId;
            Devices.RemoveRange(Devices.Except(currentDevices).ToList());
            Devices.AddRange(currentDevices.Except(Devices).ToList());

            SelectDeviceById(lastSelectedMachineIdentifier);

            await Task.Delay(250, token);
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
            settings[nameof(ApiKey)] = ApiKey;
            settings[nameof(SelectedDevice)] = SelectedDeviceId;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedDevice), out var deviceId))
                SelectDeviceById(deviceId);
            if (settings.TryGetValue<string>(nameof(ApiKey), out var apiKey))
                ApiKey = apiKey;
            if (settings.TryGetValue<Uri>(nameof(ServerBaseUri), out var serverBaseUri))
                ServerBaseUri = serverBaseUri;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (ServerBaseUri == null)
                return false;

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(500);

            var uri = new Uri(ServerBaseUri, "/System/Ping");
            var response = await UnwrapTimeout(() => client.GetAsync(uri, token));
            response.EnsureSuccessStatusCode();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> UnwrapTimeout(Func<Task<HttpResponseMessage>> action)
    {
        //https://github.com/dotnet/runtime/issues/21965

        try
        {
            return await action();
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException operationCanceledException)
            {
                var innerException = operationCanceledException.InnerException;
                if (innerException is TimeoutException)
                    innerException.Throw();

                operationCanceledException.Throw();
            }

            e.Throw();
            return null;
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ServerBaseUri
        s.RegisterAction<string>($"{Name}::ServerBaseUri::Set", s => s.WithLabel("Endpoint").WithDescription("schema://ipOrHost:port"), serverBaseUri =>
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

    internal sealed record EmbyDevice(string Name, [JsonProperty("ReportedDeviceId")] string Id, string AppName, string AppVersion)
    {
        public bool Equals(EmbyDevice other) => string.Equals(Id, other?.Id, StringComparison.Ordinal);
        public override int GetHashCode() => Id.GetHashCode();
    }

    internal sealed record EmbySession(string Id, [JsonProperty("PlayState")] PlayState State, [JsonProperty("NowPlayingItem")] PlayItem Item);
    internal sealed record PlayState(long PositionTicks, bool IsPaused, double PlaybackRate);
    internal sealed record PlayItem(long RunTimeTicks, string Path);
}
