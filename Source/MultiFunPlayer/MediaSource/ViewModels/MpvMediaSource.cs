﻿using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Windows;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("MPV")]
internal sealed class MpvMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private static string PipeName { get; } = "multifunplayer-mpv";

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && !IsDownloading;

    public FileInfo Executable { get; set; } = null;
    public string Arguments { get; set; } = "--keep-open --pause";
    public bool AutoStartEnabled { get; set; } = true;

    protected override async ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Name, PipeName, connectionType);

        if (Executable?.AsRefreshed().Exists != true)
        {
            Logger.Debug("Mpv executable not found, searching in known paths");

            var processPath = Path.GetDirectoryName(Environment.ProcessPath);
            var paths = new[]
            {
                Path.Join(processPath, "mpv.exe"),
                Path.Join(processPath, "Bin", "mpv.exe"),
                Path.Join(processPath, "Bin", "mpv", "mpv.exe")
            };

            foreach (var path in paths.TakeWhile(_ => Executable?.Exists != true))
                if (File.Exists(path))
                    Executable = new FileInfo(path);

            if (Executable?.Exists == true)
            {
                Logger.Debug("Found existing mpv executable in \"{0}\"", Executable.FullName);
            }
            else
            {
                var result = (MessageBoxResult)await DialogHelper.ShowAsync(new MessageBoxDialog("Mpv executable not found!\nWould you like to download it now?", MessageBoxButton.YesNo), "RootDialog");
                if (result != MessageBoxResult.Yes)
                    throw new MediaSourceException("Could not find mpv executable! Set path to mpv.exe manually or download latest release from settings.");

                _ = Task.Run(OnDownloadExecutable);
                return false;
            }
        }

        return true;
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            try
            {
                await client.ConnectAsync(500, token);
                Status = ConnectionStatus.Connected;
            }
            catch (TimeoutException e)
            {
                if (!AutoStartEnabled)
                    e.Throw();

                var arguments = $"--input-ipc-server={PipeName} {Arguments}";
                var processInfo = new ProcessStartInfo()
                {
                    FileName = Executable.FullName,
                    Arguments = arguments
                };

                Logger.Debug("Starting process \"{0}\" with arguments \"{1}\"", Executable.FullName, arguments);
                Process.Start(processInfo);

                if (connectionType != ConnectionType.AutoConnect)
                    Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Name, PipeName, connectionType);

                await client.ConnectAsync(2000, token);
                Status = ConnectionStatus.Connected;
            }
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0}", Name);
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            using var reader = new StreamReader(client);
            await using var writer = new StreamWriter(client) { AutoFlush = true };

            await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 1, \"pause\"] }");
            await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 2, \"duration\"] }");
            await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 3, \"time-pos\"] }");
            await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 4, \"path\"] }");
            await writer.WriteLineAsync("{ \"command\": [\"observe_property_string\", 5, \"speed\"] }");

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(client, reader, cancellationSource.Token), WriteAsync(client, writer, cancellationSource.Token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
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

        PublishMessage(new MediaPathChangedMessage(null));
        PublishMessage(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(NamedPipeClientStream client, StreamReader reader, CancellationToken token)
    {
        try
        {
            var nextPositionChangedIsSeek = false;
            while (!token.IsCancellationRequested && client.IsConnected)
            {
                var message = await reader.ReadLineAsync(token);
                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);

                if (message == null)
                    continue;

                try
                {
                    var document = JObject.Parse(message);
                    if (!document.TryGetValue<string>("event", out var eventType))
                        continue;

                    if (string.Equals(eventType, "property-change", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!document.TryGetValue<string>("name", out var propertyName) || !document.TryGetValue("data", out var dataToken))
                            continue;

                        switch (propertyName)
                        {
                            case "path":
                                PublishMessage(new MediaPathChangedMessage(dataToken.TryToObject<string>(out var path) && !string.IsNullOrWhiteSpace(path) ? path : null));
                                break;
                            case "pause":
                                if (dataToken.TryToObject<string>(out var paused))
                                    PublishMessage(new MediaPlayingChangedMessage(!string.Equals(paused, "yes", StringComparison.OrdinalIgnoreCase)));
                                break;
                            case "duration":
                                if (dataToken.TryToObject<double>(out var duration) && duration >= 0)
                                    PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                                break;
                            case "time-pos":
                                if (dataToken.TryToObject<double>(out var position) && position >= 0)
                                {
                                    PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position), ForceSeek: nextPositionChangedIsSeek));
                                    nextPositionChangedIsSeek = false;
                                }

                                break;
                            case "speed":
                                if (dataToken.TryToObject<double>(out var speed) && speed > 0)
                                    PublishMessage(new MediaSpeedChangedMessage(speed));
                                break;
                        }
                    }
                    else if (string.Equals(eventType, "seek", StringComparison.OrdinalIgnoreCase))
                    {
                        nextPositionChangedIsSeek = true;
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
                await WaitForMessageAsync(token);
                var message = await ReadMessageAsync(token);

                var messageString = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => CreateMessage("set_property", "pause", !playPauseMessage.ShouldBePlaying ? "yes" : "no"),
                    MediaSeekMessage seekMessage => CreateMessage("set_property", "time-pos", seekMessage.Position.TotalSeconds.ToString("F4").Replace(',', '.')),
                    MediaChangePathMessage changePathMessage => CreateMessage("loadfile", changePathMessage.Path.Replace('\\', '/')),
                    MediaChangeSpeedMessage changeSpeedMessage => CreateMessage("set_property", "speed", changeSpeedMessage.Speed.ToString("F4").Replace(',', '.')),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(messageString))
                    continue;

                Logger.Trace("Sending \"{0}\" to \"{1}\"", messageString, Name);
                await writer.WriteLineAsync(messageString.AsMemory(), token);
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

    public void OnLoadExecutable()
    {
        var dialog = new OpenFileDialog()
        {
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "Executable files (*.exe)|*.exe"
        };

        if (dialog.ShowDialog() != true)
            return;

        if (!string.Equals(Path.GetFileNameWithoutExtension(dialog.FileName), "mpv", StringComparison.OrdinalIgnoreCase))
            return;

        Executable = new FileInfo(dialog.FileName);
    }

    public bool IsDownloading { get; private set; }
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
}
