using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("PotPlayer")]
internal sealed class PotPlayerMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public bool AutoStartEnabled { get; set; } = true;

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} [Type: {1}]", Name, connectionType);

        if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)(?>potplayer)")))
        {
            if (!AutoStartEnabled)
                throw new MediaSourceException($"Could not find a running {Name} process");

            if (GetInstallationPath() == null)
                throw new MediaSourceException($"Could not find installed {Name} executable to auto-start");
        }

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        var process = default(Process);
        try
        {
            process = Process.GetProcesses().FirstOrDefault(p => Regex.IsMatch(p.ProcessName, "(?i)(?>potplayer)"));
            if (process == null && AutoStartEnabled)
            {
                var programPath = GetInstallationPath();
                Logger.Debug("Starting process \"{0}\"", programPath);
                process = Process.Start(programPath);
            }

            if (process == null)
                throw new MediaSourceException($"Could not find a running {Name} process");

            Status = ConnectionStatus.Connected;
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
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(process, cancellationSource.Token), WriteAsync(process, cancellationSource.Token), process.WaitForExitAsync(token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
        }
        catch (OperationCanceledException) { }
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

    private async Task ReadAsync(Process process, CancellationToken token)
    {
        var playerState = new PlayerState();

        try
        {
            var hwndSource = default(HwndSource);
            await Execute.OnUIThreadAsync(() => hwndSource = PresentationSource.FromVisual(Application.Current.MainWindow) as HwndSource);

            var window = process.MainWindowHandle;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(token) && !process.HasExited)
            {
                var state = GetValueLong(window, PlayerCommand.GetPlayState);
                if (state == -1)
                {
                    ResetState();
                    continue;
                }

                var path = await GetValueStringAsync(window, PlayerCommand.GetFilename, token);
                if (path == null)
                {
                    ResetState();
                    continue;
                }

                var duration = GetValueLong(window, PlayerCommand.GetTotalTime);
                var position = GetValueLong(window, PlayerCommand.GetCurrentTime);

                if (path != playerState.Path)
                {
                    PublishMessage(new MediaPathChangedMessage(path));
                    playerState.Path = path;
                }

                if (state != playerState.State)
                {
                    PublishMessage(new MediaPlayingChangedMessage(state == 2));
                    playerState.State = state;
                }

                if (duration != playerState.Duration)
                {
                    PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromMilliseconds(duration)));
                    playerState.Duration = duration;
                }

                if (position != playerState.Position)
                {
                    PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromMilliseconds(position)));
                    playerState.Position = position;
                }
            }

            long GetValueLong(IntPtr hWnd, PlayerCommand command)
            {
                Logger.Trace("Reading \"{0}\" from {1}", command, Name);
                return SendMessage(hWnd, 0x0400 /* WM_USER */, (UIntPtr)command, 0);
            }

            async Task<string> GetValueStringAsync(IntPtr hWnd, PlayerCommand command, CancellationToken token)
            {
                Logger.Trace("Reading \"{0}\" from {1}", command, Name);
                var completionSource = new TaskCompletionSource<string>();

                try
                {
                    hwndSource.AddHook(MessageSink);
                    SendMessage(hWnd, 0x0400 /* WM_USER */, (UIntPtr)command, hwndSource.Handle);

                    await using var _ = token.Register(() => completionSource.SetCanceled(token));
                    return await completionSource.Task;
                }
                finally
                {
                    hwndSource.RemoveHook(MessageSink);
                }

                IntPtr MessageSink(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    const int WM_COPYDATA = 0x004a;

                    if (msg == WM_COPYDATA)
                    {
                        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                        if (data.dwData == (IntPtr)command)
                            completionSource.SetResult(Marshal.PtrToStringUTF8(data.lpData));
                    }

                    return IntPtr.Zero;
                }
            }
        }
        catch (OperationCanceledException) { }

        void ResetState()
        {
            if (playerState.Path == null)
                return;

            playerState = new PlayerState();

            PublishMessage(new MediaPathChangedMessage(null));
            PublishMessage(new MediaPlayingChangedMessage(false));
        }
    }

    private async Task WriteAsync(Process process, CancellationToken token)
    {
        try
        {
            var window = process.MainWindowHandle;
            while (!process.HasExited)
            {
                await WaitForMessageAsync(token);
                var message = await ReadMessageAsync(token);

                if (message is MediaPlayPauseMessage playPauseMessage)
                    SetValueLong(window, PlayerCommand.SetPlayState, playPauseMessage.ShouldBePlaying ? 2 : 1);
                else if (message is MediaSeekMessage seekMessage)
                    SetValueLong(window, PlayerCommand.SetCurrentTime, (long)seekMessage.Position.TotalMilliseconds);
                else if (message is MediaChangePathMessage pathMessage)
                    SetValueString(window, PlayerCommand.SetFilename, pathMessage.Path, Encoding.Unicode);
            }
        }
        catch (OperationCanceledException) { }

        void SetValueLong(IntPtr hWnd, PlayerCommand command, long value)
        {
            Logger.Debug("Writing \"{0}({1})\" to {2}", command, value, Name);
            SendMessage(hWnd, 0x0400 /* WM_USER */, (UIntPtr)command, checked((IntPtr)value));
        }

        void SetValueString(IntPtr hWnd, PlayerCommand command, string value, Encoding encoding)
        {
            Logger.Debug("Writing \"{0}({1})\" to {2}", command, value, Name);

            var unmanagedBytes = IntPtr.Zero;
            var unmanagedData = IntPtr.Zero;

            try
            {
                var managedBytes = encoding.GetBytes(value ?? string.Empty);

                unmanagedBytes = Marshal.AllocCoTaskMem(managedBytes.Length + 1);
                Marshal.Copy(managedBytes, 0, unmanagedBytes, managedBytes.Length);
                Marshal.WriteByte(unmanagedBytes, managedBytes.Length, 0);

                var managedData = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)command,
                    lpData = unmanagedBytes,
                    cbData = managedBytes.Length + 1
                };

                unmanagedData = Marshal.AllocCoTaskMem(Marshal.SizeOf<COPYDATASTRUCT>());
                Marshal.StructureToPtr(managedData, unmanagedData, false);

                SendMessage(hWnd, 0x004a /* WM_COPYDATA */, UIntPtr.Zero, unmanagedData);
            }
            finally
            {
                if (unmanagedBytes != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(unmanagedBytes);
                if (unmanagedData != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(unmanagedData);
            }
        }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(AutoStartEnabled)] = AutoStartEnabled;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(AutoStartEnabled), out var autoStartEnabled))
                AutoStartEnabled = autoStartEnabled;
        }
    }

    private static string GetInstallationPath()
        => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\DAUM\PotPlayer64", "ProgramPath", null) as string
        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\DAUM\PotPlayer", "ProgramPath", null) as string;

    private sealed class PlayerState
    {
        public string Path { get; set; }
        public long? Position { get; set; }
        public long? State { get; set; }
        public long? Duration { get; set; }
    }

    private enum PlayerCommand
    {
        GetTotalTime = 0x5002,
        GetCurrentTime = 0x5004,
        SetCurrentTime = 0x5005,
        GetPlayState = 0x5006,
        SetPlayState = 0x5007,
        GetFilename = 0x6020,
        SetFilename = 0x03e8
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int dwMsg, UIntPtr wParam, IntPtr lParam = 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }
}
