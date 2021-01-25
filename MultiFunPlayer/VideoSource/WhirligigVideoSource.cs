using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Stylet;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public class WhirligigVideoSource : AbstractVideoSource
    {
        private readonly IEventAggregator _eventAggregator;

        public override string Name => "Whirligig";

        public WhirligigVideoSource(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        protected override async Task RunAsync(CancellationToken token)
        {
            try
            {
                if (!Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Whirligig")))
                    throw new Exception($"Could not find a running {Name} process.");

                using var client = new TcpClient("localhost", 2000);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);

                Status = VideoSourceStatus.Connected;
                while (!token.IsCancellationRequested && client.Connected && !reader.EndOfStream)
                {
                    var message = await reader.ReadLineAsync().WithCancellation(token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    if (message.Length >= 1 && message[0] == 'S')
                    {
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
                    }
                    else if(message.Length >= 1 && message[0] == 'P')
                    {
                        var parts = message.Split(' ', 2);
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: true));
                        _eventAggregator.Publish(new VideoPositionMessage(parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position) ? TimeSpan.FromSeconds(position) : null));
                    }
                    else if(message.Length >= 1 && message[0] == 'C')
                    {
                        var parts = message.Split(' ', 2);
                        _eventAggregator.Publish(new VideoFileChangedMessage(parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim('"') : null));
                    }
                    else if(message.Length >= 8 && message[..8] == "duration")
                    {
                        var parts = message.Split('=', 2, StringSplitOptions.TrimEntries);
                        _eventAggregator.Publish(new VideoDurationMessage(parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var duration) ? TimeSpan.FromSeconds(duration) : null));
                    }
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
                if (!Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Whirligig")))
                    return await ValueTask.FromResult(false).ConfigureAwait(false);

                using var client = new TcpClient("localhost", 2000);
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
