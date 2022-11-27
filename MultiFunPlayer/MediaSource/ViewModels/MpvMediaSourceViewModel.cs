using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Threading.Channels;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("MPV")]
internal class MpvMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>, IHandle<MediaChangePathMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly string _pipeName = "multifunplayer-mpv";
    private readonly Channel<object> _writeMessageChannel;

    public override ConnectionStatus Status { get; protected set; }

    public FileInfo Executable { get; set; } = null;
    public string Arguments { get; set; } = "--keep-open=always --pause";
    public bool AutoStartEnabled { get; set; } = false;

    public MpvMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _writeMessageChannel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true,
        });
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, _pipeName);
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(500, token);
            }
            catch (TimeoutException)
            {
                var executable = Executable?.AsRefreshed() ?? new FileInfo(Path.Join(Path.GetDirectoryName(Environment.ProcessPath), "mpv.exe"));
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
                while (_writeMessageChannel.Reader.TryRead(out _));

                using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                var task = await Task.WhenAny(ReadAsync(client, reader, cancellationSource.Token), WriteAsync(client, writer, cancellationSource.Token));
                cancellationSource.Cancel();

                task.ThrowIfFaulted();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        if (IsDisposing)
            return;

        EventAggregator.Publish(new MediaPathChangedMessage(null));
        EventAggregator.Publish(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(NamedPipeClientStream client, StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && client.IsConnected)
            {
                var message = await reader.ReadLineAsync(token);
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
                                    EventAggregator.Publish(new MediaPathChangedMessage(dataToken.TryToObject<string>(out var path) && !string.IsNullOrWhiteSpace(path) ? path : null));
                                    break;
                                case "pause":
                                    if (dataToken.TryToObject<string>(out var paused))
                                        EventAggregator.Publish(new MediaPlayingChangedMessage(!string.Equals(paused, "yes", StringComparison.OrdinalIgnoreCase)));
                                    break;
                                case "duration":
                                    if (dataToken.TryToObject<double>(out var duration) && duration >= 0)
                                        EventAggregator.Publish(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                                    break;
                                case "time-pos":
                                    if (dataToken.TryToObject<double>(out var position) && position >= 0)
                                        EventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
                                    break;
                                case "speed":
                                    if (dataToken.TryToObject<double>(out var speed) && speed > 0)
                                        EventAggregator.Publish(new MediaSpeedChangedMessage(speed));
                                    break;
                            }

                            break;
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(NamedPipeClientStream client, StreamWriter writer, CancellationToken token)
    {
        static string CreateMessage(params string[] arguments)
        {
            var argumentsString = string.Join(", ", arguments.Select(s => $"\"{s}\""));
            return $"{{ \"command\": [{argumentsString}] }}";
        }

        try
        {
            while (!token.IsCancellationRequested && client.IsConnected)
            {
                await _writeMessageChannel.Reader.WaitToReadAsync(token);
                var message = await _writeMessageChannel.Reader.ReadAsync(token);

                var messageString = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => CreateMessage("set_property", "pause", !playPauseMessage.State ? "yes" : "no"),
                    MediaSeekMessage seekMessage when seekMessage.Position.HasValue => CreateMessage("set_property", "time-pos", seekMessage.Position.Value.TotalSeconds.ToString("F4").Replace(',', '.')),
                    MediaChangePathMessage changePathMessage => CreateMessage("loadfile", changePathMessage.Path.Replace(@"\", "/")),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(messageString))
                    continue;

                Logger.Trace("Sending \"{0}\" to \"{1}\"", messageString, Name);
                await writer.WriteLineAsync(messageString).WithCancellation(token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Executable)] = Executable != null ? JToken.FromObject(Executable) : null;
            settings[nameof(Arguments)] = Arguments;
            settings[nameof(AutoStartEnabled)] = AutoStartEnabled;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<FileInfo>(nameof(Executable), out var executable))
                Executable = executable;
            if (settings.TryGetValue<string>(nameof(Arguments), out var arguments))
                Arguments = arguments;
            if (settings.TryGetValue<bool>(nameof(AutoStartEnabled), out var autoStartEnabled))
                AutoStartEnabled = autoStartEnabled;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(AutoStartEnabled || File.Exists(@$"\\.\\pipe\\{_pipeName}"));

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

    public bool IsDownloading { get; set; } = false;
    public async void OnDownloadExecutable()
    {
        IsDownloading = true;
        Executable = null;

        try
        {
            var downloadRoot = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "bin", "mpv"));
            if (downloadRoot.Exists)
                downloadRoot.Delete(true);

            downloadRoot = Directory.CreateDirectory(downloadRoot.FullName);
            var bootstrapperUri = new Uri("https://sourceforge.net/projects/mpv-player-windows/files/bootstrapper.zip/download");
            var bootstrapperZip = new FileInfo(Path.Combine(downloadRoot.FullName, "bootstrapper.zip"));

            {
                using var client = NetUtils.CreateHttpClient();
                await client.DownloadFileAsync(bootstrapperUri, bootstrapperZip.FullName);
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
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} executable download failed with exception", "RootDialog");
        }

        IsDownloading = false;
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Arguments
        s.RegisterAction<string>($"{Name}::Arguments::Set", s => s.WithLabel("Arguments") , arguments => Arguments = arguments);
        #endregion
    }

    public async void Handle(MediaSeekMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    public async void Handle(MediaPlayPauseMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    public async void Handle(MediaChangePathMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }
}
