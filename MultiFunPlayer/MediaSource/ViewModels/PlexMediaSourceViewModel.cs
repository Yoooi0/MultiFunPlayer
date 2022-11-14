using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Xml;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Plex")]
internal class PlexMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Channel<object> _writeMessageChannel;
    private SemaphoreSlim _refreshSemaphore;
    private CancellationTokenSource _refreshCancellationSource;
    private XmlNode _currentTimeline;
    private long _commandId;

    public override ConnectionStatus Status { get; protected set; }

    public ObservableConcurrentCollection<PlexClient> Clients { get; }
    public PlexClient SelectedClient { get; set; } = null;
    public string SelectedClientMachineIdentifier { get; set; } = null;
    public EndPoint ServerEndpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 32400);
    public string PlexToken { get; set; } = null;
    private string ClientIdentifier { get; set; } = Guid.NewGuid().ToString();

    public PlexMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _refreshSemaphore = new SemaphoreSlim(1, 1);
        _refreshCancellationSource = new CancellationTokenSource();
        _writeMessageChannel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true,
        });

        Clients = new ObservableConcurrentCollection<PlexClient>();
        SelectedClient = null;

        _ = RefreshClients();
    }

    public void OnSelectedClientChanged() => SelectedClientMachineIdentifier = SelectedClient?.MachineIdentifier;

    public bool CanChangeClient => !IsConnected && !IsConnectBusy;
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task<bool> OnConnectingAsync()
    {
        if (SelectedClientMachineIdentifier == null)
            return false;
        if (SelectedClient == null)
            await RefreshClients();

        return SelectedClient != null && await base.OnConnectingAsync();
    }

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, ServerEndpoint);
            var client = NetUtils.CreateHttpClient();

            await Task.Delay(250, token);
            Status = ConnectionStatus.Connected;

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(client, cancellationSource.Token), WriteAsync(client, cancellationSource.Token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
        }
        catch (OperationCanceledException) { }
        catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        EventAggregator.Publish(new MediaPathChangedMessage(null));
        EventAggregator.Publish(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(HttpClient httpClient, CancellationToken token)
    {
        try
        {
            _commandId = 0L;
            var lastMetadataUri = default(Uri);
            var basePollUri = new Uri($"http://{ServerEndpoint.ToUriString()}/player/timeline/poll");

            while (!token.IsCancellationRequested)
            {
                _currentTimeline = await GetCurrentTimelineAsync();
                if (_currentTimeline == null)
                {
                    ResetState();
                    continue;
                }

                var timeAttribute = _currentTimeline.Attributes["time"];
                var keyAttribute = _currentTimeline.Attributes["key"];
                var stateAttribute = _currentTimeline.Attributes["state"];
                if (timeAttribute == null || keyAttribute == null || stateAttribute == null)
                {
                    ResetState();
                    continue;
                }

                var metadataKey = keyAttribute.Value;
                var streamIdAttribute = _currentTimeline.Attributes["audioStreamID"] ?? _currentTimeline.Attributes["videoStreamID"];
                var streamId = streamIdAttribute != null ? int.Parse(streamIdAttribute.Value) : default(int?);
                if (!await UpdateMetadataIfNecessaryAsync(metadataKey, streamId))
                {
                    ResetState();
                    continue;
                }

                var state = string.Equals(stateAttribute.Value, "playing", StringComparison.OrdinalIgnoreCase);
                var position = TimeSpan.FromMilliseconds(int.Parse(timeAttribute.Value));
                EventAggregator.Publish(new MediaPositionChangedMessage(position));
                EventAggregator.Publish(new MediaPlayingChangedMessage(state));
            }

            async Task<bool> UpdateMetadataIfNecessaryAsync(string metadataKey, int? streamId)
            {
                var messageUri = new Uri($"http://{ServerEndpoint.ToUriString()}{metadataKey}");
                if (messageUri == lastMetadataUri)
                    return true;

                var response = await WriteCommandAsync(messageUri, token);
                var document = new XmlDocument();
                document.Load(await response.Content.ReadAsStreamAsync(token));

                var root = document.DocumentElement;
                Logger.Trace(() => string.Format("Received \"{0}\" from \"{1}\"", root.OuterXml, Name));

                if (!root.HasChildNodes)
                    return false;

                var partNode = streamId switch
                {
                    int id => root.SelectSingleNode($"//Stream[@id={id}]/parent::Part"),
                    _ => root.SelectSingleNode("//Track[1]/Media[1]/Part[1]")
                };

                if (partNode == null)
                    return false;

                var durationAttribute = partNode.Attributes["duration"];
                var fileAttribute = partNode.Attributes["file"];
                if (durationAttribute == null || fileAttribute == null)
                    return false;

                var duration = TimeSpan.FromMilliseconds(int.Parse(durationAttribute.Value));
                var path = fileAttribute.Value;

                if (string.IsNullOrEmpty(path))
                    return false;

                EventAggregator.Publish(new MediaPathChangedMessage(path));
                EventAggregator.Publish(new MediaDurationChangedMessage(duration));

                lastMetadataUri = messageUri;
                return true;
            }

            async Task<XmlNode> GetCurrentTimelineAsync()
            {
                var response = await WriteTimelineCommandAsync();
                var document = new XmlDocument();
                document.Load(await response.Content.ReadAsStreamAsync(token));

                var root = document.DocumentElement;
                Logger.Trace(() => string.Format("Received \"{0}\" from \"{1}\"", root.OuterXml, Name));

                if (!root.HasChildNodes)
                    return null;

                return root.ChildNodes
                           .OfType<XmlNode>()
                           .Where(n => !string.Equals(n.Attributes["type"]?.Value, "photo", StringComparison.OrdinalIgnoreCase))
                           .FirstOrDefault(n => !string.Equals(n.Attributes["state"]?.Value, "stopped", StringComparison.OrdinalIgnoreCase));

                async Task<HttpResponseMessage> WriteTimelineCommandAsync()
                {
                    var timelineCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timelineCancellationSource.CancelAfter(TimeSpan.FromSeconds(5));

                    try
                    {
                        var messageUri = new Uri(basePollUri, $"?wait=1&commandID={Interlocked.Increment(ref _commandId)}");
                        return await WriteCommandAsync(messageUri, timelineCancellationSource.Token);
                    }
                    catch (OperationCanceledException) when (timelineCancellationSource.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        // send non-blocking command to avoid connection reset exception while paused
                        var messageUri = new Uri(basePollUri, $"?wait=0&commandID={Interlocked.Increment(ref _commandId)}");
                        return await WriteCommandAsync(messageUri, token);
                    }
                    finally
                    {
                        timelineCancellationSource.Cancel();
                        timelineCancellationSource.Dispose();
                        timelineCancellationSource = null;
                    }
                }
            }

            async Task<HttpResponseMessage> WriteCommandAsync(Uri uri, CancellationToken token)
            {
                var message = new HttpRequestMessage(HttpMethod.Get, uri);

                message.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", SelectedClient.MachineIdentifier);
                AddDefaultHeaders(message.Headers);

                return await UnwrapTimeout(() => httpClient.SendAsync(message, token));
            }

            void ResetState()
            {
                _currentTimeline = null;
                lastMetadataUri = null;
                EventAggregator.Publish(new MediaPathChangedMessage(null));
                EventAggregator.Publish(new MediaPlayingChangedMessage(false));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(HttpClient httpClient, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _writeMessageChannel.Reader.WaitToReadAsync(token);

                if (_currentTimeline == null)
                    continue;

                var mtype = _currentTimeline.Attributes["type"].Value;
                var message = await _writeMessageChannel.Reader.ReadAsync(token);

                var commonArguments = $"type={mtype}&commandID={Interlocked.Increment(ref _commandId)}";
                var messageUriPath = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => playPauseMessage.State ? $"/player/playback/play?{commonArguments}" : $"/player/playback/pause?{commonArguments}",
                    MediaSeekMessage seekMessage when seekMessage.Position.HasValue => $"/player/playback/seekTo?{commonArguments}&offset={(int)seekMessage.Position.Value.TotalMilliseconds}",
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(messageUriPath))
                    continue;

                var messageUri = new Uri($"http://{ServerEndpoint.ToUriString()}{messageUriPath}");
                Logger.Info("Sending \"{0}\" to \"{1}\"", messageUriPath, Name);

                var requestMessage = new HttpRequestMessage(HttpMethod.Get, messageUri);

                Logger.Info(SelectedClient.ProtocolCapabilities);
                requestMessage.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", SelectedClient.MachineIdentifier);
                AddDefaultHeaders(requestMessage.Headers);

                var messageCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                messageCancellationSource.CancelAfter(TimeSpan.FromSeconds(1));

                try
                {
                    _ = await UnwrapTimeout(() => httpClient.SendAsync(requestMessage, messageCancellationSource.Token));
                }
                catch (OperationCanceledException) when (messageCancellationSource.IsCancellationRequested && !token.IsCancellationRequested) { }
                finally
                {
                    messageCancellationSource.Cancel();
                    messageCancellationSource.Dispose();
                    messageCancellationSource = null;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public bool CanRefreshClients => !IsRefreshBusy && !IsConnected && !IsConnectBusy && ServerEndpoint != null;
    public bool IsRefreshBusy { get; set; }
    public async Task RefreshClients()
    {
        var token = _refreshCancellationSource.Token;

        if (_refreshSemaphore.CurrentCount == 0)
            return;
        if (ServerEndpoint == null)
            return;

        await _refreshSemaphore.WaitAsync(token);
        IsRefreshBusy = true;
        await Task.Delay(250, token).ConfigureAwait(true);

        try
        {
            using var httpClient = NetUtils.CreateHttpClient();

            var message = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://{ServerEndpoint.ToUriString()}/clients"));
            AddDefaultHeaders(message.Headers);

            var response = await httpClient.SendAsync(message, token);
            var document = new XmlDocument();
            document.Load(await response.Content.ReadAsStreamAsync(token));

            var root = document.DocumentElement;
            if (!root.HasChildNodes)
                return;

            var currentClients = root.ChildNodes.OfType<XmlNode>().Select(n => new PlexClient(
                Name: n.Attributes["name"].Value,
                Host: n.Attributes["host"].Value,
                Address: n.Attributes["address"].Value,
                Port: int.TryParse(n.Attributes["port"].Value, out var port) ? port : 32433,
                MachineIdentifier: n.Attributes["machineIdentifier"].Value,
                Version: n.Attributes["version"].Value,
                Protocol: n.Attributes["protocol"].Value,
                Product: n.Attributes["product"].Value,
                DeviceClass: n.Attributes["deviceClass"].Value,
                ProtocolVersion: int.TryParse(n.Attributes["protocolVersion"].Value, out var protocolVersion) ? protocolVersion : 1,
                ProtocolCapabilities: n.Attributes["protocolCapabilities"].Value
            ));

            var lastSelectedMachineIdentifier = SelectedClientMachineIdentifier;
            Clients.RemoveRange(Clients.Except(currentClients).ToList());
            Clients.AddRange(currentClients.Except(Clients).ToList());

            SelectClientByMachineIdentifier(SelectedClientMachineIdentifier);
        }
        catch (Exception e)
        {
            Logger.Warn(e, $"{Name} client refresh failed with exception");
        }

        await Task.Delay(250, token).ConfigureAwait(true);
        IsRefreshBusy = false;
        _refreshSemaphore.Release();
    }

    private void SelectClientByMachineIdentifier(string machineIdentifier)
    {
        SelectedClient = Clients.FirstOrDefault(p => string.Equals(p.MachineIdentifier, machineIdentifier, StringComparison.Ordinal));
        if (SelectedClient == null)
            SelectedClientMachineIdentifier = machineIdentifier;
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(ServerEndpoint)] = JToken.FromObject(ServerEndpoint);
            settings[nameof(PlexToken)] = PlexToken;
            settings[nameof(ClientIdentifier)] = ClientIdentifier;
            settings[nameof(SelectedClient)] = SelectedClientMachineIdentifier;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedClient), out var machineIdentifier))
                SelectClientByMachineIdentifier(machineIdentifier);
            if (settings.TryGetValue<EndPoint>(nameof(ServerEndpoint), out var endpoint))
                ServerEndpoint = endpoint;
            if (settings.TryGetValue<string>(nameof(PlexToken), out var plexToken))
                PlexToken = plexToken;
            if (settings.TryGetValue<string>(nameof(ClientIdentifier), out var clientIdentifier))
                ClientIdentifier = clientIdentifier;
        }
    }

    private void AddDefaultHeaders(HttpRequestHeaders headers)
    {
        headers.TryAddWithoutValidation("X-Plex-Platform", "Windows");
        headers.TryAddWithoutValidation("X-Plex-Platform-Version", Environment.OSVersion.Version.Major.ToString());
        headers.TryAddWithoutValidation("X-Plex-Device", "Windows");
        headers.TryAddWithoutValidation("X-Plex-Device-Name", Environment.MachineName);
        headers.TryAddWithoutValidation("X-Plex-Client-Identifier", ClientIdentifier);
        headers.TryAddWithoutValidation("X-Plex-Provides", "controller");
        headers.TryAddWithoutValidation("X-Plex-Product", nameof(MultiFunPlayer));
        headers.TryAddWithoutValidation("X-Plex-Version", ReflectionUtils.AssemblyVersion.ToString());
        headers.TryAddWithoutValidation("X-Plex-Sync-Version", "2");
        headers.TryAddWithoutValidation("X-Plex-Features", "external-media");
        headers.TryAddWithoutValidation("X-Plex-Token", PlexToken);
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        if (ServerEndpoint == null)
            return false;

        using var httpClient = NetUtils.CreateHttpClient();

        var message = new HttpRequestMessage(HttpMethod.Head, new Uri($"http://{ServerEndpoint.ToUriString()}/clients"));
        AddDefaultHeaders(message.Headers);

        var response = await httpClient.SendAsync(message, token);
        return response.IsSuccessStatusCode;
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

    public async void Handle(MediaSeekMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    public async void Handle(MediaPlayPauseMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    protected override void Dispose(bool disposing)
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        _refreshSemaphore?.Dispose();
        _refreshSemaphore = null;

        base.Dispose(disposing);
    }

    internal record class PlexClient(string Name, string Host, string Address, int Port, string MachineIdentifier, string Version, string Protocol,
                                     string Product, string DeviceClass, int ProtocolVersion, string ProtocolCapabilities);
}
