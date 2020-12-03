using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Stylet;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.Player
{
    public class MpvVideoPlayer : PropertyChangedBase, IVideoPlayer
    {
        private readonly IEventAggregator _eventAggregator;
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public string Name => "MPV";
        public VideoPlayerStatus Status { get; private set; }

        public MpvVideoPlayer(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void Start()
        {
            if (Status != VideoPlayerStatus.Disconnected)
                Stop();

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(async () => await RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public void Stop()
        {
            if (Status != VideoPlayerStatus.Connected)
                return;

            Status = VideoPlayerStatus.Disconnected;

            _cancellationSource?.Cancel();
            _task?.Wait();
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                const string pipeName = "/tmp/mfp-mpv";
                using var client = new NamedPipeClientStream(pipeName);

                try
                {
                    await client.ConnectAsync(500, token);
                }
                catch (TimeoutException)
                {
                    var mpvPath = Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "mpv.exe");
                    if (!File.Exists(mpvPath))
                    {
                        _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"Could not find mpv executable!\n\nPlease download or copy it to:\n\"{mpvPath}\"")));
                    }
                    else
                    {
                        var processInfo = new ProcessStartInfo()
                        {
                            FileName = mpvPath,
                            Arguments = $"--input-ipc-server={pipeName} --keep-open=always --pause"
                        };

                        Process.Start(processInfo);

                        await Task.Delay(1000, token);
                        await client.ConnectAsync(500, token);
                    }
                }

                if (client.IsConnected)
                {
                    using var reader = new StreamReader(client);
                    using var writer = new StreamWriter(client) { AutoFlush = true };

                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 1, \"pause\"] }");
                    await reader.ReadLineAsync();
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 2, \"duration\"] }");
                    await reader.ReadLineAsync();
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 3, \"time-pos\"] }");
                    await reader.ReadLineAsync();
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 4, \"path\"] }");
                    await reader.ReadLineAsync();

                    Status = VideoPlayerStatus.Connected;
                    while (!token.IsCancellationRequested && client.IsConnected)
                    {
                        var message = await reader.ReadLineAsync().WithCancellation(token);
                        if (message == null)
                            continue;

                        var document = JsonDocument.Parse(message);
                        if (!document.RootElement.TryGetProperty("event", out var eventProperty))
                            continue;

                        switch (eventProperty.GetString())
                        {
                            case "property-change":
                                {
                                    if (!document.RootElement.TryGetProperty("name", out var nameProperty)
                                     || !document.RootElement.TryGetProperty("data", out var dataProperty))
                                        continue;

                                    var isNull = dataProperty.ValueKind == JsonValueKind.Null;
                                    switch (nameProperty.GetString())
                                    {
                                        case "path":
                                            var path = isNull ? null : dataProperty.GetString();
                                            _eventAggregator.Publish(new VideoFileChangedMessage(path));
                                            break;
                                        case "time-pos":
                                            var position = isNull ? double.NaN : double.Parse(dataProperty.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture);
                                            _eventAggregator.Publish(new VideoPositionMessage(double.IsFinite(position) ? TimeSpan.FromSeconds(position) : null));
                                            break;
                                        case "pause":
                                            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: isNull ? false : dataProperty.GetString() != "yes"));
                                            break;
                                        case "duration":
                                            var duration = isNull ? double.NaN : double.Parse(dataProperty.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture);
                                            _eventAggregator.Publish(new VideoDurationMessage(double.IsFinite(duration) ? TimeSpan.FromSeconds(duration) : null));
                                            break;
                                        default: break;
                                    }
                                    break;
                                }
                            default: break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"MPV failed with exception:\n\n{e}")));
            }

            Status = VideoPlayerStatus.Disconnected;
            _cancellationSource?.Dispose();

            _eventAggregator.Publish(new VideoFileChangedMessage(null));
            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
