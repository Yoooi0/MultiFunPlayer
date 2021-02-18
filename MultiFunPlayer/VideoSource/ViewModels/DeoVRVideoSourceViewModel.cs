using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private readonly IEventAggregator _eventAggregator;

        public override string Name => "DeoVR";
        public override VideoSourceStatus Status { get; protected set; }

        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 23554);

        public DeoVRVideoSourceViewModel(IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public bool IsConnected => Status == VideoSourceStatus.Connected;
        public bool IsConnectBusy => Status == VideoSourceStatus.Connecting || Status == VideoSourceStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override async Task RunAsync(CancellationToken token)
        {
            static async Task<byte[]> ReadAllBytesAsync(NetworkStream stream, CancellationToken token)
            {
                var result = 0;
                var buffer = new ArraySegment<byte>(new byte[1024]);
                using var memory = new MemoryStream();
                do
                {
                    result = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    await memory.WriteAsync(buffer.AsMemory(buffer.Offset, result), token).ConfigureAwait(false);
                }
                while (result > 0 && stream.DataAvailable);

                memory.Seek(0, SeekOrigin.Begin);
                return memory.ToArray();
            }

            try
            {
                if (Endpoint == null)
                    throw new Exception($"Endpoint cannot be null.");

                if (string.Equals(Endpoint.Address, "localhost") || string.Equals(Endpoint.Address, "127.0.0.1"))
                    if (Process.GetProcessesByName("DeoVR").Length == 0)
                        throw new Exception($"Could not find a running {Name} process.");

                using var client = new TcpClient();
                {
                    using var timeoutCancellationSource = new CancellationTokenSource(5000);
                    using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                    await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token).ConfigureAwait(false);
                }

                using var stream = client.GetStream();

                _ = Task.Factory.StartNew(async () =>
                {
                    var pingBuffer = new byte[4];
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                        await stream.WriteAsync(pingBuffer, token).ConfigureAwait(false);
                        await stream.FlushAsync(token).ConfigureAwait(false);
                    }
                }, token);

                Status = VideoSourceStatus.Connected;
                while (!token.IsCancellationRequested && client.Connected)
                {
                    var data = await ReadAllBytesAsync(stream, token).ConfigureAwait(false);
                    if (data.Length <= 4)
                        continue;

                    var length = BitConverter.ToInt32(data[0..4], 0);
                    if (length <= 0 || data.Length != length + 4)
                        continue;

                    try
                    {
                        var document = JObject.Parse(Encoding.UTF8.GetString(data[4..(length+4)]));

                        if (document.TryGetValue("path", out var pathToken))
                            _eventAggregator.Publish(new VideoFileChangedMessage(pathToken.TryToObject<string>(out var path) && !string.IsNullOrWhiteSpace(path) ? path : null));

                        if (document.TryGetValue("playerState", out var stateToken) && stateToken.TryToObject<int>(out var state))
                            _eventAggregator.Publish(new VideoPlayingMessage(state == 0));

                        if (document.TryGetValue("duration", out var durationToken) && durationToken.TryToObject<float>(out var duration) && duration >= 0)
                            _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));

                        if (document.TryGetValue("currentTime", out var timeToken) && timeToken.TryToObject<float>(out var position) && position >= 0)
                            _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(position)));

                        if (document.TryGetValue("playbackSpeed", out var speedToken) && speedToken.TryToObject<float>(out var speed) && speed > 0)
                            _eventAggregator.Publish(new VideoSpeedMessage(speed));
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}")));
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
                if (settings.TryGetValue(nameof(Endpoint), out var endpointToken) && IPEndPoint.TryParse(endpointToken.ToObject<string>(), out var endpoint))
                    Endpoint = endpoint;
            }
        }

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
        {
            try
            {
                if (Endpoint == null)
                    return await ValueTask.FromResult(false).ConfigureAwait(false);

                if(string.Equals(Endpoint.Address.ToString(), "localhost") || string.Equals(Endpoint.Address.ToString(), "127.0.0.1"))
                    if (Process.GetProcessesByName("DeoVR").Length == 0)
                        return await ValueTask.FromResult(false).ConfigureAwait(false);

                using var client = new TcpClient();
                {
                    using var timeoutCancellationSource = new CancellationTokenSource(2500);
                    using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                    await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token).ConfigureAwait(false);
                }

                using var stream = client.GetStream();

                return await ValueTask.FromResult(client.Connected).ConfigureAwait(false);
            }
            catch
            {
                return await ValueTask.FromResult(false).ConfigureAwait(false);
            }
        }
    }
}
