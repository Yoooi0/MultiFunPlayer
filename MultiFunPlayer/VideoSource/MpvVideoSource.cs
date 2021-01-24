using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource
{
    public class MpvVideoSource : AbstractVideoSource
    {
        private readonly string _pipeName = "multifunplayer-mpv";
        private readonly IEventAggregator _eventAggregator;

        public override string Name => "MPV";

        public MpvVideoSource(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        protected override async Task RunAsync(CancellationToken token)
        {
            try
            {
                using var client = new NamedPipeClientStream(_pipeName);

                try
                {
                    await client.ConnectAsync(500, token).ConfigureAwait(false);
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
                            Arguments = $"--input-ipc-server={_pipeName} --keep-open=always --pause"
                        };

                        Process.Start(processInfo);

                        await client.ConnectAsync(2000, token).ConfigureAwait(false);
                    }
                }

                if (client.IsConnected)
                {
                    using var reader = new StreamReader(client);
                    using var writer = new StreamWriter(client) { AutoFlush = true };

                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 1, \"pause\"] }").ConfigureAwait(false);
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 2, \"duration\"] }").ConfigureAwait(false);
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 3, \"time-pos\"] }").ConfigureAwait(false);
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 4, \"path\"] }").ConfigureAwait(false);
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 5, \"speed\"] }").ConfigureAwait(false);

                    Status = VideoSourceStatus.Connected;
                    while (!token.IsCancellationRequested && client.IsConnected)
                    {
                        var message = await reader.ReadLineAsync().WithCancellation(token).ConfigureAwait(false);
                        if (message == null)
                            continue;

                        try
                        {
                            var document = JObject.Parse(message);
                            if (!document.TryGetValue("event", out var eventToken))
                                continue;

                            switch (eventToken.ToObject<string>())
                            {
                                case "property-change":
                                    {
                                        if (!document.TryGetValue("name", out var nameToken)
                                         || !document.TryGetValue("data", out var dataToken))
                                            continue;

                                        switch (nameToken.ToObject<string>())
                                        {
                                            case "path":
                                                _eventAggregator.Publish(new VideoFileChangedMessage(dataToken.TryToObject<string>(out var path) ? path : null));
                                                break;
                                            case "time-pos":
                                                _eventAggregator.Publish(new VideoPositionMessage(dataToken.TryToObject<double>(out var position) ? TimeSpan.FromSeconds(position) : null));
                                                break;
                                            case "pause":
                                                _eventAggregator.Publish(new VideoPlayingMessage(dataToken.TryToObject<string>(out var paused) && paused != "yes"));
                                                break;
                                            case "duration":
                                                _eventAggregator.Publish(new VideoDurationMessage(dataToken.TryToObject<double>(out var duration) ? TimeSpan.FromSeconds(duration) : null));
                                                break;
                                            case "speed":
                                                if (dataToken.TryToObject<double>(out var speed))
                                                    _eventAggregator.Publish(new VideoSpeedMessage((float)speed));
                                                break;
                                            default: break;
                                        }
                                        break;
                                    }
                                default: break;
                            }
                        }
                        catch (JsonException) { }
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

        public override async ValueTask<bool> CanStartAsync(CancellationToken token) => await ValueTask.FromResult(File.Exists(@$"\\.\\pipe\\{_pipeName}")).ConfigureAwait(false);
    }
}
