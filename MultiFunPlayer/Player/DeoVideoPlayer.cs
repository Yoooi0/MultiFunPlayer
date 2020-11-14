using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.Player
{
    public class DeoVideoPlayer : PropertyChangedBase, IVideoPlayer
    {
        private readonly IEventAggregator _eventAggregator;
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public string Name => "DeoVR";
        public VideoPlayerStatus Status { get; private set; }

        public DeoVideoPlayer(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void Start()
        {
            if (_cancellationSource != null)
            {
                _cancellationSource?.Cancel();
                _task?.Wait();
                _cancellationSource?.Dispose();
            }

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(async () => await RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private async Task RunAsync(CancellationToken token)
        {
            static async Task<string> ReadStringAsync(NetworkStream stream, CancellationToken token)
            {
                var result = 0;
                var buffer = new ArraySegment<byte>(new byte[1024]);
                using var memory = new MemoryStream();
                do
                {
                    result = await stream.ReadAsync(buffer, token);
                    await memory.WriteAsync(buffer.AsMemory(buffer.Offset, result), token);
                }
                while (result > 0 && stream.DataAvailable);

                memory.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(memory, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }

            try
            {
                if (Process.GetProcessesByName("DeoVR").Length == 0)
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "steam://launch/837380/VR",
                        UseShellExecute = true
                    });
                    await Task.Delay(10000, token);
                }

                using var client = new TcpClient("localhost", 23554);
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

                Status = VideoPlayerStatus.Connected;
                while (!token.IsCancellationRequested && client.Connected)
                {
                    var data = await ReadStringAsync(stream, token);
                    if (string.IsNullOrWhiteSpace(data))
                        break;

                    var length = BitConverter.ToInt32(Encoding.ASCII.GetBytes(data[0..4]), 0);
                    if (length <= 0)
                        continue;

                    data = data[4..];
                    var document = JsonDocument.Parse(data);
                    if (document.RootElement.TryGetProperty("path", out var pathProperty))
                        _eventAggregator.Publish(new VideoFileChangedMessage(new FileInfo(pathProperty.GetString())));

                    if (document.RootElement.TryGetProperty("currentTime", out var timeProperty))
                        _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(timeProperty.GetDouble())));

                    if (document.RootElement.TryGetProperty("playerState", out var stateProperty))
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: stateProperty.GetInt32() == 0));
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"DeoVR failed with exception:\n\n{e}")));
            }

            Status = VideoPlayerStatus.Disconnected;
            _cancellationSource?.Dispose();
            _cancellationSource = null;

            _eventAggregator.Publish(new VideoFileChangedMessage(null));
            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
        }

        protected virtual void Dispose(bool disposing)
        {
            Status = VideoPlayerStatus.Disconnected;

            _cancellationSource?.Cancel();
            _task?.Wait();
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
