using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
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

namespace MultiFunPlayer.VideoSource.ViewModels;

[DisplayName("MPV")]
public class MpvVideoSourceViewModel : AbstractVideoSource, IHandle<VideoPlayPauseMessage>, IHandle<VideoSeekMessage>
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly string _pipeName = "multifunplayer-mpv";
    private readonly IEventAggregator _eventAggregator;
    private readonly Channel<object> _writeMessageChannel;

    public override ConnectionStatus Status { get; protected set; }

    public FileInfo Executable { get; set; } = null;
    public string Arguments { get; set; } = "--keep-open=always --pause";
    public bool AutoStartEnabled { get; set; } = false;

    public MpvVideoSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _eventAggregator = eventAggregator;
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
            Logger.Info("Connecting to {0}", Name);
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
                while (_writeMessageChannel.Reader.TryRead(out _)) ;

                using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                var task = await Task.WhenAny(ReadAsync(client, reader, cancellationSource.Token), WriteAsync(client, writer, cancellationSource.Token));
                cancellationSource.Cancel();

                if (task.Exception != null)
                    throw task.Exception;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = Execute.OnUIThreadAsync(() => DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog"));
        }

        _eventAggregator.Publish(new VideoFileChangedMessage(null));
        _eventAggregator.Publish(new VideoPlayingMessage(isPlaying: false));
    }

    private async Task ReadAsync(NamedPipeClientStream client, StreamReader reader, CancellationToken token)
    {
        try
        {
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
                                    if (dataToken.TryToObject<string>(out var paused))
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
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(NamedPipeClientStream client, StreamWriter writer, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && client.IsConnected)
            {
                await _writeMessageChannel.Reader.WaitToReadAsync(token);
                var message = await _writeMessageChannel.Reader.ReadAsync(token);

                var messageString = message switch
                {
                    VideoPlayPauseMessage playPauseMessage => $"{{ \"command\": [\"set_property\", \"pause\", {(playPauseMessage.State ? "false" : "true")}] }}",
                    VideoSeekMessage seekMessage when seekMessage.Position.HasValue => $"{{ \"command\": [\"set_property\", \"time-pos\", {seekMessage.Position.Value.TotalSeconds.ToString("F4").Replace(',', '.')}] }}",
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

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        if (type == AppSettingsMessageType.Saving)
        {
            if (Executable != null)
                settings[nameof(Executable)] = JToken.FromObject(Executable);
            if (Arguments != null)
                settings[nameof(Arguments)] = new JValue(Arguments);

            settings[nameof(AutoStartEnabled)] = new JValue(AutoStartEnabled);
        }
        else if (type == AppSettingsMessageType.Loading)
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

    public void OnClearExecutable() => Executable = null;

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
                using var client = WebUtils.CreateClient();
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
            _ = Execute.OnUIThreadAsync(() => DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} executable download failed with exception:\n\n{e}"), "RootDialog"));
        }

        IsDownloading = false;
    }

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        #region Arguments
        s.RegisterAction($"{Name}::Arguments::Set", b => b.WithSetting<string>(s => s.WithLabel("Arguments")).WithCallback((_, arguments) => Arguments = arguments));
        #endregion
    }

    public async void Handle(VideoSeekMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }

    public async void Handle(VideoPlayPauseMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _writeMessageChannel.Writer.WriteAsync(message);
    }
}
