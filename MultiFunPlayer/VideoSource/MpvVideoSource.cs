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

namespace MultiFunPlayer.VideoSource
{
    public class MpvVideoSource : AbstractVideoSource
    {
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
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 2, \"duration\"] }");
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 3, \"time-pos\"] }");
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 4, \"path\"] }");
                    await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 5, \"speed\"] }");

                    static bool TryReadDouble(JsonElement element, out double value)
                    {
                        value = double.NaN;
                        if (element.ValueKind == JsonValueKind.Null)
                            return false;
                        return double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                    }

                    static bool TryReadString(JsonElement element, out string value)
                    {
                        value = null;
                        if (element.ValueKind == JsonValueKind.Null)
                            return false;
                        value = element.GetString();
                        return true;
                    }

                    Status = VideoSourceStatus.Connected;
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

                                    switch (nameProperty.GetString())
                                    {
                                        case "path":
                                            _eventAggregator.Publish(new VideoFileChangedMessage(TryReadString(dataProperty, out var path) ? path : null));
                                            break;
                                        case "time-pos":
                                            _eventAggregator.Publish(new VideoPositionMessage(TryReadDouble(dataProperty, out var position) ? TimeSpan.FromSeconds(position) : null));
                                            break;
                                        case "pause":
                                            _eventAggregator.Publish(new VideoPlayingMessage(TryReadString(dataProperty, out var paused) && paused != "yes"));
                                            break;
                                        case "duration":
                                            _eventAggregator.Publish(new VideoDurationMessage(TryReadDouble(dataProperty, out var duration) ? TimeSpan.FromSeconds(duration) : null));
                                            break;
                                        case "speed":
                                            if(TryReadDouble(dataProperty, out var speed))
                                                _eventAggregator.Publish(new VideoSpeedMessage((float)speed));
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
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}")));
            }

            Status = VideoSourceStatus.Disconnected;

            _eventAggregator.Publish(new VideoFileChangedMessage(null));
            _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
        }
    }
}
