using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Controls.ViewModels;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Messages;
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
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.ViewModels
{
    public class DeoVRVideoSourceViewModel : AbstractVideoSource
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IEventAggregator _eventAggregator;

        public override string Name => "DeoVR";
        public override ConnectionStatus Status { get; protected set; }

        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 23554);

        public DeoVRVideoSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
            : base(shortcutManager, eventAggregator)
        {
            _eventAggregator = eventAggregator;
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

                _ = Task.Factory.StartNew(async () =>
                {
                    var pingBuffer = new byte[4];
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(500, token);
                        await stream.WriteAsync(pingBuffer, token);
                        await stream.FlushAsync(token);
                    }
                }, token);

                var lastSpeed = default(float?);
                var lastDuration = default(float?);
                var lastPosition = default(float?);
                var lastState = default(int?);
                var lastPath = default(string);

                Status = ConnectionStatus.Connected;
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

                            if (path != lastPath)
                            {
                                _eventAggregator.Publish(new VideoFileChangedMessage(path));
                                lastPath = path;
                            }
                        }

                        if (document.TryGetValue("playerState", out var stateToken) && stateToken.TryToObject<int>(out var state) && state != lastState)
                        {
                            _eventAggregator.Publish(new VideoPlayingMessage(state == 0));
                            lastState = state;
                        }

                        if (document.TryGetValue("duration", out var durationToken) && durationToken.TryToObject<float>(out var duration) && duration >= 0 && duration != lastDuration)
                        {
                            _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));
                            lastDuration = duration;
                        }

                        if (document.TryGetValue("currentTime", out var timeToken) && timeToken.TryToObject<float>(out var position) && position >= 0 && position != lastPosition)
                        {
                            _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(position)));
                            lastPosition = position;
                        }

                        if (document.TryGetValue("playbackSpeed", out var speedToken) && speedToken.TryToObject<float>(out var speed) && speed > 0 && speed != lastSpeed)
                        {
                            _eventAggregator.Publish(new VideoSpeedMessage(speed));
                            lastSpeed = speed;
                        }
                    }
                    catch (JsonException) { }
                }
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
    }
}
