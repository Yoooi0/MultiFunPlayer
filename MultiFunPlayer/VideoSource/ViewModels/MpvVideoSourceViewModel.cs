using MaterialDesignThemes.Wpf;
using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.ViewModels
{
    public class MpvVideoSourceViewModel : AbstractVideoSource
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _pipeName = "multifunplayer-mpv";
        private readonly IEventAggregator _eventAggregator;

        public override string Name => "MPV";
        public override ConnectionStatus Status { get; protected set; }

        public FileInfo Executable { get; set; } = null;
        public string Arguments { get; set; } = "--keep-open=always --pause";

        public MpvVideoSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
            : base(shortcutManager, eventAggregator)
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
                using var client = new NamedPipeClientStream(_pipeName);

                try
                {
                    await client.ConnectAsync(500, token);
                }
                catch (TimeoutException)
                {
                    var executable = Executable ?? new FileInfo(Path.Join(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "mpv.exe"));
                    if (!executable.Exists)
                    {
                        throw new Exception("Could not find mpv executable! Please set path to mpv.exe or download latest release from settings.");
                    }
                    else
                    {
                        var processInfo = new ProcessStartInfo()
                        {
                            FileName = executable.FullName,
                            Arguments = $"--input-ipc-server={_pipeName} {Arguments}"
                        };

                        Process.Start(processInfo);

                        await client.ConnectAsync(2000, token);
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

                    Status = ConnectionStatus.Connected;
                    while (!token.IsCancellationRequested && client.IsConnected)
                    {
                        var message = await reader.ReadLineAsync().WithCancellation(token);
                        if (message == null)
                            continue;

                        try
                        {
                            Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);

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
            catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
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
            if (type == AppSettingsMessageType.Saving)
            {
                if(Executable != null)
                    settings[nameof(Executable)] = JToken.FromObject(Executable);
                if(Arguments != null)
                    settings[nameof(Arguments)] = new JValue(Arguments);
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue<FileInfo>(nameof(Executable), out var executable))
                    Executable = executable;
                if (settings.TryGetValue<string>(nameof(Arguments), out var arguments))
                    Arguments = arguments;
            }
        }

        public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(File.Exists(@$"\\.\\pipe\\{_pipeName}"));

        public void OnLoadExecutable()
        {
            var dialog = new CommonOpenFileDialog()
            {
                EnsureFileExists = true
            };
            dialog.Filters.Add(new CommonFileDialogFilter("Executable files", "*.exe"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            if (!string.Equals(Path.GetFileNameWithoutExtension(dialog.FileName), "mpv", StringComparison.OrdinalIgnoreCase))
                return;

            Executable = new FileInfo(dialog.FileName);
        }

        public void OnClearExecutable() => Executable = null;

        public bool IsDownloading { get; set; } = false;
        public async void OnDownloadExecutable()
        {
            IsDownloading = true;
            Executable = null;

            try
            {
                var downloadRoot = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "bin", "mpv"));
                if (downloadRoot.Exists)
                    downloadRoot.Delete(true);

                downloadRoot = Directory.CreateDirectory(downloadRoot.FullName);
                var bootstrapperUri = new Uri("https://sourceforge.net/projects/mpv-player-windows/files/bootstrapper.zip/download");
                var bootstrapperZip = new FileInfo(Path.Combine(downloadRoot.FullName, "bootstrapper.zip"));

                {
                    using var client = new WebClient();
                    await client.DownloadFileTaskAsync(bootstrapperUri, bootstrapperZip.FullName);
                }

                ZipFile.ExtractToDirectory(bootstrapperZip.FullName, downloadRoot.FullName, true);
                bootstrapperZip.Delete();

                var updater = new FileInfo(Path.Combine(downloadRoot.FullName, "updater.bat"));
                using var process = new Process
                {
                    StartInfo =
                    {
                        FileName = updater.FullName,
                        UseShellExecute = true
                    },
                    EnableRaisingEvents = true
                };

                var completionSource = new TaskCompletionSource<int>();
                process.Exited += (s, e) => completionSource.SetResult(process.ExitCode);
                process.Start();

                var result = await completionSource.Task;
                if (result == 0)
                    Executable = new FileInfo(Path.Combine(downloadRoot.FullName, "mpv.exe"));

                foreach (var file in downloadRoot.EnumerateFiles("mpv*.7z"))
                    file.Delete();

                if (Executable?.Exists == false)
                    Executable = null;
            }
            catch (Exception e)
            {
                Logger.Error(e, $"{Name} executable download failed with exception");
                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"{Name} executable download failed with exception:\n\n{e}")));
            }

            IsDownloading = false;
        }
    }
}
