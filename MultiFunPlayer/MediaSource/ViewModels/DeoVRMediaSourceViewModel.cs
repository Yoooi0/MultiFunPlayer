using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("DeoVR")]
internal class DeoVRMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>, IHandle<MediaChangePathMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Channel<object> _writeMessageChannel;

    public override ConnectionStatus Status { get; protected set; }

    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 23554);

    public DeoVRMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _writeMessageChannel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true,
        });
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, Endpoint);
            if (Endpoint == null)
                throw new Exception("Endpoint cannot be null.");

            if (Endpoint.IsLocalhost())
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)(?>deovr|slr)")))
                    throw new Exception($"Could not find a running {Name} process.");

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(5000);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();

            Status = ConnectionStatus.Connected;
            while (_writeMessageChannel.Reader.TryRead(out _));

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(client, stream, cancellationSource.Token), WriteAsync(client, stream, cancellationSource.Token));
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

        EventAggregator.Publish(new MediaPathChangedMessage(null));
        EventAggregator.Publish(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(TcpClient client, NetworkStream stream, CancellationToken token)
    {
        try
        {
            var playerState = default(PlayerState);
            while (!token.IsCancellationRequested && client.Connected)
            {
                var lengthBuffer = await stream.ReadBytesAsync(4, token);
                if (lengthBuffer.Length < 4)
                    continue;

                var length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length <= 0)
                {
                    Logger.Trace("Received \"\" from \"{0}\"", Name);

                    if (playerState != null)
                    {
                        EventAggregator.Publish(new MediaPathChangedMessage(null));
                        EventAggregator.Publish(new MediaPlayingChangedMessage(false));
                        playerState = null;
                    }

                    continue;
                }

                playerState ??= new PlayerState();

                var dataBuffer = await stream.ReadBytesAsync(length, token);
                try
                {
                    var json = Encoding.UTF8.GetString(dataBuffer);
                    var document = JObject.Parse(json);
                    Logger.Trace("Received \"{0}\" from \"{1}\"", json, Name);

                    if (document.TryGetValue("path", out var pathToken) && pathToken.TryToObject<string>(out var path))
                    {
                        if (string.IsNullOrWhiteSpace(path))
                            path = null;

                        if (path != playerState.Path)
                        {
                            EventAggregator.Publish(new MediaPathChangedMessage(path));
                            playerState.Path = path;
                        }
                    }

                    if (document.TryGetValue("playerState", out var stateToken) && stateToken.TryToObject<int>(out var state) && state != playerState.State)
                    {
                        EventAggregator.Publish(new MediaPlayingChangedMessage(state == 0));
                        playerState.State = state;
                    }

                    if (document.TryGetValue("duration", out var durationToken) && durationToken.TryToObject<double>(out var duration) && duration >= 0 && duration != playerState.Duration)
                    {
                        EventAggregator.Publish(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                        playerState.Duration = duration;
                    }

                    if (document.TryGetValue("currentTime", out var timeToken) && timeToken.TryToObject<double>(out var position) && position >= 0 && position != playerState.Position)
                    {
                        EventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
                        playerState.Position = position;
                    }

                    if (document.TryGetValue("playbackSpeed", out var speedToken) && speedToken.TryToObject<double>(out var speed) && speed > 0 && speed != playerState.Speed)
                    {
                        EventAggregator.Publish(new MediaSpeedChangedMessage(speed));
                        playerState.Speed = speed;
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(TcpClient client, NetworkStream stream, CancellationToken token)
    {
        try
        {
            var keepAliveBuffer = new byte[4];
            while (!token.IsCancellationRequested && client.Connected)
            {
                using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);

                var readMessageTask = _writeMessageChannel.Reader.WaitToReadAsync(cancellationSource.Token).AsTask();
                var timeoutTask = Task.Delay(1000, cancellationSource.Token);
                var completedTask = await Task.WhenAny(readMessageTask, timeoutTask);

                cancellationSource.Cancel();
                if (completedTask.Exception != null)
                    throw completedTask.Exception;

                if (completedTask == readMessageTask)
                {
                    var message = await _writeMessageChannel.Reader.ReadAsync(token);
                    var sendState = new PlayerState();

                    if (message is MediaPlayPauseMessage playPauseMessage)
                        sendState.State = playPauseMessage.ShouldBePlaying ? 0 : 1;
                    else if (message is MediaSeekMessage seekMessage && seekMessage.Position.HasValue)
                        sendState.Position = seekMessage.Position.Value.TotalSeconds;
                    else if (message is MediaChangePathMessage changePathMessage)
                        sendState.Path = changePathMessage.Path;

                    var messageString = JsonConvert.SerializeObject(sendState);

                    Logger.Debug("Sending \"{0}\" to \"{1}\"", messageString, Name);

                    var messageBytes = Encoding.UTF8.GetBytes(messageString);
                    var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

                    var bytes = new byte[lengthBytes.Length + messageBytes.Length];
                    Array.Copy(lengthBytes, 0, bytes, 0, lengthBytes.Length);
                    Array.Copy(messageBytes, 0, bytes, lengthBytes.Length, messageBytes.Length);

                    await stream.WriteAsync(bytes, token);
                }
                else if (completedTask == timeoutTask)
                {
                    Logger.Trace("Sending keep-alive to \"{0}\"", Name);

                    await stream.WriteAsync(keepAliveBuffer, token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Endpoint)] = Endpoint?.ToString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (Endpoint == null)
                return false;

            if (Endpoint.IsLocalhost())
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)(?>deovr|slr)")))
                    return false;

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(2500);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();

            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Name}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ip/host:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        });
        #endregion
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

    public async void Handle(MediaChangePathMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    private class PlayerState
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("currentTime")] public double? Position { get; set; }
        [JsonProperty("playbackSpeed")] public double? Speed { get; set; }
        [JsonProperty("playerState")] public int? State { get; set; }
        [JsonProperty("duration")] public double? Duration { get; set; }
    }
}
