using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Xml;
using Newtonsoft.Json.Linq;
using MultiFunPlayer.Shortcut;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Plex")]
internal sealed class PlexMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private CancellationTokenSource _refreshCancellationSource = new();
    private XmlNode _currentTimeline;
    private long _commandId;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SelectedClient != null && !string.IsNullOrWhiteSpace(PlexToken);

    public ObservableConcurrentCollection<PlexClient> Clients { get; } = [];
    public PlexClient SelectedClient { get; set; } = null;
    public string SelectedClientMachineIdentifier { get; set; } = null;
    public Uri ServerBaseUri { get; set; } = new Uri("http://127.0.0.1:32400");
    public string PlexToken { get; set; } = null;
    private string ClientIdentifier { get; set; } = Guid.NewGuid().ToString();

    public bool CanChangeClient => IsDisconnected && !IsRefreshBusy && !string.IsNullOrWhiteSpace(PlexToken) && Clients.Count != 0;
    public void OnSelectedClientChanged()
    {
        SelectedClientMachineIdentifier = SelectedClient?.MachineIdentifier;
        if (SelectedClientMachineIdentifier == null)
            return;

        Logger.Debug("Selected {0}", SelectedClient);
    }

    protected override void OnInitialActivate()
    {
        base.OnInitialActivate();
        _ = RefreshClients();
    }

    protected override async ValueTask<bool> OnConnectingAsync()
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
            Logger.Info("Connecting to {0} at \"{1}\"", Name, ServerBaseUri);
            if (ServerBaseUri == null)
                throw new Exception("Endpoint cannot be null.");
            if (string.IsNullOrEmpty(PlexToken))
                throw new Exception("Plex token cannot be empty.");

            var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(5000);

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

        if (IsDisposing)
            return;

        PublishMessage(new MediaPathChangedMessage(null));
        PublishMessage(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(HttpClient httpClient, CancellationToken token)
    {
        try
        {
            _commandId = 0L;
            var lastMetadataUri = default(Uri);
            var basePollUri = new Uri(ServerBaseUri, "/player/timeline/poll");

            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested)
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
                PublishMessage(new MediaPositionChangedMessage(position));
                PublishMessage(new MediaPlayingChangedMessage(state));
            }

            async Task<bool> UpdateMetadataIfNecessaryAsync(string metadataKey, int? streamId)
            {
                var messageUri = new Uri(ServerBaseUri, metadataKey);
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

                PublishMessage(new MediaPathChangedMessage(path));
                PublishMessage(new MediaDurationChangedMessage(duration));

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
                           .Where(n => string.Equals(n.Name, "Timeline", StringComparison.OrdinalIgnoreCase))
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
                Logger.Trace("Sending \"{0}\" to \"{1}\"", uri, Name);

                message.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", SelectedClient.MachineIdentifier);
                AddDefaultHeaders(message.Headers);

                return await UnwrapTimeout(() => httpClient.SendAsync(message, token));
            }

            void ResetState()
            {
                _currentTimeline = null;
                lastMetadataUri = null;
                PublishMessage(new MediaPathChangedMessage(null));
                PublishMessage(new MediaPlayingChangedMessage(false));
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
                await WaitForMessageAsync(token);

                if (_currentTimeline == null)
                    continue;

                var mtype = _currentTimeline.Attributes["type"].Value;
                var message = await ReadMessageAsync(token);

                var commonArguments = $"type={mtype}&commandID={Interlocked.Increment(ref _commandId)}";
                var messageUriPath = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => playPauseMessage.ShouldBePlaying ? $"/player/playback/play?{commonArguments}" : $"/player/playback/pause?{commonArguments}",
                    MediaSeekMessage seekMessage => $"/player/playback/seekTo?{commonArguments}&offset={(int)seekMessage.Position.TotalMilliseconds}",
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(messageUriPath))
                    continue;

                var messageUri = new Uri(ServerBaseUri, messageUriPath);
                Logger.Info("Sending \"{0}\" to \"{1}\"", messageUriPath, Name);

                var requestMessage = new HttpRequestMessage(HttpMethod.Get, messageUri);

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

    public bool CanRefreshClients => !IsRefreshBusy && IsDisconnected && ServerBaseUri != null && !string.IsNullOrWhiteSpace(PlexToken);
    public bool IsRefreshBusy { get; set; }

    private int _isRefreshingFlag;
    public async Task RefreshClients()
    {
        if (string.IsNullOrWhiteSpace(PlexToken))
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
            Logger.Debug("Refreshing clients");

            using var client = NetUtils.CreateHttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(5000);

            var message = new HttpRequestMessage(HttpMethod.Get, new Uri(ServerBaseUri, "/clients"));
            AddDefaultHeaders(message.Headers);

            var response = await client.SendAsync(message, token);
            response.EnsureSuccessStatusCode();

            var document = new XmlDocument();
            document.Load(await response.Content.ReadAsStreamAsync(token));

            var root = document.DocumentElement;
            Logger.Trace(() => string.Format("Received \"{0}\" from \"{1}\"", root.OuterXml, Name));

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

            NotifyOfPropertyChange(nameof(Clients));
            SelectClientByMachineIdentifier(lastSelectedMachineIdentifier);

            await Task.Delay(250, token);
        }
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
            settings[nameof(ServerBaseUri)] = JToken.FromObject(ServerBaseUri);
            settings[nameof(PlexToken)] = PlexToken;
            settings[nameof(ClientIdentifier)] = ClientIdentifier;
            settings[nameof(SelectedClient)] = SelectedClientMachineIdentifier;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedClient), out var machineIdentifier))
                SelectClientByMachineIdentifier(machineIdentifier);
            if (settings.TryGetValue<Uri>(nameof(ServerBaseUri), out var serverBaseUri))
                ServerBaseUri = serverBaseUri;
            if (settings.TryGetValue<string>(nameof(PlexToken), out var plexToken))
                PlexToken = plexToken;
            if (settings.TryGetValue<string>(nameof(ClientIdentifier), out var clientIdentifier))
                ClientIdentifier = clientIdentifier;
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

        #region PlexToken
        s.RegisterAction<string>($"{Name}::PlexToken::Set", s => s.WithLabel("Token"), token => PlexToken = token);
        #endregion

        #region SelectedClient
        s.RegisterAction<string>($"{Name}::Client::SetByName", s => s.WithLabel("Name"), name => {
            var client = Clients.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (client == null)
                return;

            SelectClientByMachineIdentifier(client.MachineIdentifier);
        });
        #endregion
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
        headers.TryAddWithoutValidation("X-Plex-Version", GitVersionInformation.MajorMinorPatch);
        headers.TryAddWithoutValidation("X-Plex-Sync-Version", "2");
        headers.TryAddWithoutValidation("X-Plex-Features", "external-media");
        headers.TryAddWithoutValidation("X-Plex-Token", PlexToken);
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        if (ServerBaseUri == null)
            return false;

        using var client = NetUtils.CreateHttpClient();
        client.Timeout = TimeSpan.FromMilliseconds(5000);

        var message = new HttpRequestMessage(HttpMethod.Head, new Uri(ServerBaseUri, "/clients"));
        AddDefaultHeaders(message.Headers);

        var response = await client.SendAsync(message, token);
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

    protected override void Dispose(bool disposing)
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        base.Dispose(disposing);
    }

    internal sealed record class PlexClient(string Name, string Host, string Address, int Port, string MachineIdentifier, string Version, string Protocol,
                                     string Product, string DeviceClass, int ProtocolVersion, string ProtocolCapabilities);
}
