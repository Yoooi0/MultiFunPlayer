using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace MultiFunPlayer.VideoSource
{
    public class WhirligigVideoSource : PropertyChangedBase, IVideoSource
    {
        private readonly IEventAggregator _eventAggregator;
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        public string Name => "Whirligig";
        public VideoSourceStatus Status { get; private set; }

        public WhirligigVideoSource(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void Start()
        {
            Stop();

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => Stop());
        }

        public void Stop()
        {
            Status = VideoSourceStatus.Disconnected;

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
                if (!Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Whirligig")))
                    throw new Exception("Could not find a running Whirligig process.");

                using var client = new TcpClient("localhost", 2000);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);

                Status = VideoSourceStatus.Connected;
                while (!token.IsCancellationRequested && client.Connected && !reader.EndOfStream)
                {
                    var message = await reader.ReadLineAsync().WithCancellation(token);
                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    var parts = message.Split(' ');
                    if (message.Length >= 1 && message[0] == 'S')
                    {
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
                    }
                    else if(message.Length >= 1 && message[0] == 'P')
                    {
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: true));
                        _eventAggregator.Publish(new VideoPositionMessage(parts.Length == 2 && double.TryParse(parts[1], out var position) ? TimeSpan.FromSeconds(position) : null));
                    }
                    else if(message.Length >= 1 && message[0] == 'C')
                    {
                        _eventAggregator.Publish(new VideoFileChangedMessage(parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim('"') : null));
                    }
                    else if(message.Length >= 8 && message[..8] == "duration")
                    {
                        _eventAggregator.Publish(new VideoDurationMessage(double.TryParse(parts.Last(), out var duration) ? TimeSpan.FromSeconds(duration) : null));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"Whirligig failed with exception:\n\n{e}")));
            }

            Status = VideoSourceStatus.Disconnected;

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
