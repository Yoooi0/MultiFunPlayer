using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Net;
using System.Net.Http;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Jellyfin")]
internal sealed class JellyfinMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private CancellationTokenSource _refreshCancellationSource = new();
    private JellyfinSession _currentSession;

    public override ConnectionStatus Status { get; protected set; }

    public EndPoint ServerEndpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8096);
    public string ApiKey { get; set; }
    public JellyfinDevice SelectedDevice { get; set; } = null;
    public string SelectedDeviceId { get; set; }
    public ObservableConcurrentCollection<JellyfinDevice> Devices { get; set; } = [];

    public void OnSelectedDeviceChanged() => SelectedDeviceId = SelectedDevice?.Id;

    protected override void OnInitialActivate()
    {
        base.OnInitialActivate();
        _ = RefreshDevices();
    }

    public bool CanChangeDevice => !IsConnected && !IsConnectBusy;
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task<bool> OnConnectingAsync()
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
            Logger.Info("Connecting to {0} at \"{1}\"", Name, ServerEndpoint);
            if (ServerEndpoint == null)
                throw new Exception("Endpoint cannot be null.");
            if (string.IsNullOrEmpty(ApiKey))
                throw new Exception("Api key cannot be empty.");

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(1000);

            var uri = new Uri($"http://{ServerEndpoint.ToUriString()}/System/Ping");
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

                var sessionsUri = new Uri($"http://{ServerEndpoint.ToUriString()}/Sessions?api_key={ApiKey}&DeviceId={SelectedDeviceId}");
                var response = await UnwrapTimeout(() => client.GetAsync(sessionsUri, token));
                if (response == null)
                    continue;

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

                var baseUri = new Uri($"http://{ServerEndpoint.ToUriString()}/Sessions/{_currentSession.Id}/Playing/");
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

    public bool CanRefreshDevices => !IsRefreshBusy && !IsConnected && !IsConnectBusy && ServerEndpoint != null && ApiKey != null;
    public bool IsRefreshBusy { get; set; }

    private int _isRefreshingFlag;
    public async Task RefreshDevices()
    {
        if (ServerEndpoint == null)
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
            await DoRefreshClients(token);
        }
        catch (Exception e)
        {
            Logger.Warn(e, $"{Name} client refresh failed with exception");
        }
        finally
        {
            Interlocked.Decrement(ref _isRefreshingFlag);
            IsRefreshBusy = false;
        }

        async Task DoRefreshClients(CancellationToken token)
        {
            await Task.Delay(250, token);

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(5000);

            var uri = new Uri($"http://{ServerEndpoint.ToUriString()}/Devices?api_key={ApiKey}");
            var response = await UnwrapTimeout(() => client.GetAsync(uri, token));
            var content = await response.Content.ReadAsStringAsync(token);

            var o = JObject.Parse(content);
            var currentDevices = o["Items"].ToObject<List<JellyfinDevice>>();

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
            settings[nameof(ServerEndpoint)] = ServerEndpoint?.ToString();
            settings[nameof(ApiKey)] = ApiKey;
            settings[nameof(SelectedDevice)] = SelectedDeviceId;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedDevice), out var deviceId))
                SelectDeviceById(deviceId);
            if (settings.TryGetValue<string>(nameof(ApiKey), out var apiKey))
                ApiKey = apiKey;
            if (settings.TryGetValue<EndPoint>(nameof(ServerEndpoint), out var endpoint))
                ServerEndpoint = endpoint;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (ServerEndpoint == null)
                return false;

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(500);

            var uri = new Uri($"http://{ServerEndpoint.ToUriString()}/System/Ping");
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

        #region ServerEndpoint
        s.RegisterAction<string>($"{Name}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ip/host:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                ServerEndpoint = endpoint;
        });
        #endregion
    }

    protected override void Dispose(bool disposing)
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        base.Dispose(disposing);
    }

    internal sealed record class JellyfinDevice()
    {
        public string Name { get; init; }
        public string Id { get; init; }
        public string AppName { get; init; }
        public string AppVersion { get; init; }
    }

    internal sealed record class JellyfinSession()
    {
        public string Id { get; init; }
        [JsonProperty("PlayState")]
        public PlayState State { get; init; }
        [JsonProperty("NowPlayingItem")]
        public PlayItem Item { get; init; }
    }

    internal sealed record class PlayState(long PositionTicks, bool IsPaused);
    internal sealed record class PlayItem(long RunTimeTicks, string Path);
}
