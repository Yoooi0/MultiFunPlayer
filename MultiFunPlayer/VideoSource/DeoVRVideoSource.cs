using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public class DeoVRVideoSource : AbstractVideoSource
    {
        private readonly IEventAggregator _eventAggregator;

        public override string Name => "DeoVR";

        public DeoVRVideoSource(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

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
                if (Process.GetProcessesByName("DeoVR").Length == 0)
                    throw new Exception($"Could not find a running {Name} process.");

                using var client = new TcpClient("localhost", 23554);
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
                        var document = JObject.Parse(data.AsMemory(4..(length+4)).ToString());

                        if (document.TryGetValue("playerState", out var stateToken) && stateToken.TryToObject<int>(out var state))
                            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: state == 0));

                        if (document.TryGetValue("duration", out var durationToken) && durationToken.TryToObject<float>(out var duration))
                            _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));

                        if (document.TryGetValue("currentTime", out var timeToken) && timeToken.TryToObject<float>(out var time))
                            _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(time)));

                        if (document.TryGetValue("path", out var pathToken) && pathToken.TryToObject<string>(out var path))
                            _eventAggregator.Publish(new VideoFileChangedMessage(path));

                        if (document.TryGetValue("playbackSpeed", out var speedToken) && speedToken.TryToObject<float>(out var speed))
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

        public override async ValueTask<bool> CanStartAsync(CancellationToken token)
        {
            try
            {
                if (Process.GetProcessesByName("DeoVR").Length == 0)
                    return await ValueTask.FromResult(false).ConfigureAwait(false);

                using var client = new TcpClient("localhost", 23554);
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
