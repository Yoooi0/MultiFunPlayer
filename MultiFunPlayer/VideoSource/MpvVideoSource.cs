using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.VideoSource.Settings;
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
        private readonly MpvVideoSourceSettingsViewModel _settings;

        public override string Name => "MPV";
        public override object SettingsViewModel => _settings;

        public MpvVideoSource(IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _settings = new MpvVideoSourceSettingsViewModel();
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
                    var executable = _settings.Executable ?? new FileInfo(Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "mpv.exe"));
                    if (!executable.Exists)
                    {
                        _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"Could not find mpv executable!\n\nYou can download latest release from settings or copy mpv.exe to\n\"{executable.FullName}\"\n")));
                    }
                    else
                    {
                        var processInfo = new ProcessStartInfo()
                        {
                            FileName = executable.FullName,
                            Arguments = $"--input-ipc-server={_pipeName} {_settings.Arguments}"
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
                                    if (!document.TryGetValue("name", out var nameToken)
                                        || !document.TryGetValue("data", out var dataToken))
                                        continue;

                                    switch (nameToken.ToObject<string>())
                                    {
                                        case "path":
                                            _eventAggregator.Publish(new VideoFileChangedMessage(dataToken.TryToObject<string>(out var path) && !string.IsNullOrWhiteSpace(path) ? path : null));
                                            break;
                                        case "pause":
                                            if(dataToken.TryToObject<string>(out var paused))
                                                _eventAggregator.Publish(new VideoPlayingMessage(!string.Equals(paused, "yes", StringComparison.OrdinalIgnoreCase)));
                                            break;
                                        case "duration":
                                            if (dataToken.TryToObject<float>(out var duration) && duration >= 0)
                                                _eventAggregator.Publish(new VideoDurationMessage(TimeSpan.FromSeconds(duration)));
                                            break;
                                        case "time-pos":
                                            if (dataToken.TryToObject<float>(out var position) && position >= 0)
                                                _eventAggregator.Publish(new VideoPositionMessage(TimeSpan.FromSeconds(position)));
                                            break;
                                        case "speed":
                                            if (dataToken.TryToObject<float>(out var speed) && speed > 0)
                                                _eventAggregator.Publish(new VideoSpeedMessage(speed));
                                            break;
                                    }
                                    break;
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

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(File.Exists(@$"\\.\\pipe\\{_pipeName}")).ConfigureAwait(false);
    }
}
