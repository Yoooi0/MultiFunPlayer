using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.ViewModels
{
    public class DeoVRVideoSourceViewModel : AbstractVideoSource, IHandle<VideoPlayPauseMessage>, IHandle<VideoSeekMessage>
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IEventAggregator _eventAggregator;
        private readonly Channel<object> _writeMessageChannel;

        public override string Name => "DeoVR";
        public override ConnectionStatus Status { get; protected set; }

        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 23554);

        public DeoVRVideoSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
            : base(shortcutManager, eventAggregator)
        {
            _eventAggregator = eventAggregator;
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
                Logger.Info("Connecting to {0}", Name);
                if (Endpoint == null)
                    throw new Exception("Endpoint cannot be null.");

                if (string.Equals(Endpoint.Address.ToString(), "localhost") || string.Equals(Endpoint.Address.ToString(), "127.0.0.1"))
                    if (Process.GetProcessesByName("DeoVR").Length == 0)
                        throw new Exception($"Could not find a running {Name} process.");

                using var client = new TcpClient();
                {
                    using var timeoutCancellationSource = new CancellationTokenSource(5000);
                    using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                    await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token);
                }

                using var stream = client.GetStream();

                Status = ConnectionStatus.Connected;
                while (_writeMessageChannel.Reader.TryRead(out _)) ;

                using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                var task = await Task.WhenAny(ReadAsync(client, stream, cancellationSource.Token), WriteAsync(client, stream, cancellationSource.Token));
                cancellationSource.Cancel();

                if (task.Exception != null)
                    throw task.Exception;
            }
            catch (OperationCanceledException) { }
            catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
            catch (Exception e)
            {
                Logger.Error(e, $"{Name} failed with exception");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog"));
            }

            _eventAggregator.Publish(new VideoFileChangedMessage(null));
            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
        }

        private async Task ReadAsync(TcpClient client, NetworkStream stream, CancellationToken token)
        {
            try
            {
                var playerState = new PlayerState();
                while (!token.IsCancellationRequested && client.Connected)
                {
                    var data = await stream.ReadAllBytesAsync(token);
                    if (data.Length <= 4)
                        continue;

                    var length = BitConverter.ToInt32(data[0..4], 0);
                    if (length <= 0 || data.Length != length + 4)
                        continue;

                    try
                    {
                        var json = Encoding.UTF8.GetString(data[4..(length + 4)]);
                        var document = JObject.Parse(json);
                        Logger.Trace("Received \"{0}\" from \"{1}\"", json, Name);

                        if (document.TryGetValue("path", out var pathToken) && pathToken.TryToObject<string>(out var path))
                        {
                            if (string.IsNullOrWhiteSpace(path))
                                path = null;

                            if (path != playerState.Path)
                            {
                                _eventAggregator.Publish(new VideoFileChangedMessage(path));
                                playerState.Path = path;
                            }
                        }

                        if (document.TryGetValue("playerState", out var stateToken) && stateToken.TryToObject<int>(out var state) && state != playerState.State)
                        {
                            _eventAggregator.Publish(new VideoPlayingMessage(state == 0));
                            playerState.State = state;
                        }

                        if (document.TryGetValue("duration", out var durationToken) && durationToken.TryToObject<float>(out var duration) && duration >= 0 && duration != playerState.Duration)
                        {
                            _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));
                            playerState.Duration = duration;
                        }

                        if (document.TryGetValue("currentTime", out var timeToken) && timeToken.TryToObject<float>(out var position) && position >= 0 && position != playerState.Position)
                        {
                            _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(position)));
                            playerState.Position = position;
                        }

                        if (document.TryGetValue("playbackSpeed", out var speedToken) && speedToken.TryToObject<float>(out var speed) && speed > 0 && speed != playerState.Speed)
                        {
                            _eventAggregator.Publish(new VideoSpeedMessage(speed));
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
                var pingBuffer = new byte[4];
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

                        if (message is VideoPlayPauseMessage playPauseMessage)
                            sendState.State = playPauseMessage.State ? 0 : 1;
                        else if (message is VideoSeekMessage seekMessage && seekMessage.Position.HasValue)
                            sendState.Position = (float)seekMessage.Position.Value.TotalSeconds;

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
                        await stream.WriteAsync(pingBuffer, token);
                        await stream.FlushAsync(token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                if(Endpoint != null)
                    settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue<string>(nameof(Endpoint), out var endpointString) && IPEndPoint.TryParse(endpointString, out var endpoint))
                    Endpoint = endpoint;
            }
        }

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
        {
            try
            {
                if (Endpoint == null)
                    return await ValueTask.FromResult(false);

                if(string.Equals(Endpoint.Address.ToString(), "localhost") || string.Equals(Endpoint.Address.ToString(), "127.0.0.1"))
                    if (Process.GetProcessesByName("DeoVR").Length == 0)
                        return await ValueTask.FromResult(false);

                using var client = new TcpClient();
                {
                    using var timeoutCancellationSource = new CancellationTokenSource(2500);
                    using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                    await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token);
                }

                using var stream = client.GetStream();

                return await ValueTask.FromResult(client.Connected);
            }
            catch
            {
                return await ValueTask.FromResult(false);
            }
        }

        protected override void RegisterShortcuts(IShortcutManager s)
        {
            base.RegisterShortcuts(s);

            #region Endpoint
            s.RegisterAction<string>($"{Name}::Endpoint::Set", "Endpoint", (_, endpointString) =>
            {
                if (IPEndPoint.TryParse(endpointString, out var endpoint))
                    Endpoint = endpoint;
            });
            #endregion
        }

        public async void Handle(VideoSeekMessage message)
        {
            if (Status == ConnectionStatus.Connected)
                await _writeMessageChannel.Writer.WriteAsync(message);
        }

        public async void Handle(VideoPlayPauseMessage message)
        {
            if (Status == ConnectionStatus.Connected)
                await _writeMessageChannel.Writer.WriteAsync(message);
        }

        private class PlayerState
        {
            [JsonProperty("path")] public string Path { get; set; }
            [JsonProperty("currentTime")] public float? Position { get; set; }
            [JsonProperty("playbackSpeed")] public float? Speed { get; set; }
            [JsonProperty("playerState")] public int? State { get; set; }
            [JsonProperty("duration")] public float? Duration { get; set; }
        }
    }
}
