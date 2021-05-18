using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.ViewModels
{
    public class WhirligigVideoSourceViewModel : AbstractVideoSource
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IEventAggregator _eventAggregator;

        public override string Name => "Whirligig";
        public override ConnectionStatus Status { get; protected set; }

        public WhirligigVideoSourceViewModel(IEventAggregator eventAggregator) : base(eventAggregator)
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
                if (!Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Whirligig")))
                    throw new Exception($"Could not find a running {Name} process.");

                using var client = new TcpClient("localhost", 2000);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);

                Status = ConnectionStatus.Connected;
                while (!token.IsCancellationRequested && client.Connected && !reader.EndOfStream)
                {
                    var message = await reader.ReadLineAsync().WithCancellation(token);
                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);
                    if (message.Length >= 1 && message[0] == 'C')
                    {
                        var parts = message.Split(' ', 2);
                        _eventAggregator.Publish(new VideoFileChangedMessage(parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim('"') : null));
                    }
                    else if (message.Length >= 1 && message[0] == 'S')
                    {
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
                    }
                    else if (message.Length >= 8 && message[..8] == "duration")
                    {
                        var parts = message.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var duration) && duration >= 0)
                            _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));
                    }
                    else if (message.Length >= 1 && message[0] == 'P')
                    {
                        var parts = message.Split(' ', 2);
                        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: true));

                        if (parts.Length == 2 && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position) && position >= 0)
                            _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(position)));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                Logger.Error(e, $"{Name} failed with exception");
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}")));
            }

            _eventAggregator.Publish(new VideoFileChangedMessage(null));
            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
        }

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
        {
            try
            {
                if (!Process.GetProcesses().Any(p => p.ProcessName.StartsWith("Whirligig")))
                    return await ValueTask.FromResult(false);

                using var client = new TcpClient("localhost", 2000);
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
