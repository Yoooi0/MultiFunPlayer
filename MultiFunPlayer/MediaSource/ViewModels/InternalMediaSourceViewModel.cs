using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Internal")]
public class InternalMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly Channel<object> _messageChannel;

    private bool _isPlaying;
    private double _position;
    private double _duration;
    private FileInfo _scriptInfo;

    public override ConnectionStatus Status { get; protected set; }
    public int PlaylistIndex { get; set; } = 0;
    public List<FileInfo> ScriptPlaylist { get; set; } = null;

    public bool IsShuffling { get; set; } = false;
    public bool IsLooping { get; set; } = false;

    public InternalMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _messageChannel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
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

            await Task.Delay(250, token);
            SetPlaylist(null);

            Status = ConnectionStatus.Connected;

            var lastTicks = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                var currentTicks = Stopwatch.GetTimestamp();
                var elapsed = (currentTicks - lastTicks) / (double)Stopwatch.Frequency;
                lastTicks = currentTicks;

                while (_messageChannel.Reader.TryRead(out var message))
                {
                    if (message is MediaPlayPauseMessage playPauseMessage) { SetIsPlaying(playPauseMessage.State); }
                    else if (message is MediaSeekMessage seekMessage && _scriptInfo != null) { SetPosition(seekMessage.Position?.TotalSeconds ?? double.NaN); }
                    else if (message is ScriptPlaylistMessage playlistMessage) { SetPlaylist(playlistMessage.Playlist); }
                    else if (message is PlayScriptAtIndexMessage playIndexMessage) { PlayByIndex(playIndexMessage.Index); }
                    else if (message is PlayScriptWithOffsetMessage playOffsetMessage)
                    {
                        if (ScriptPlaylist == null)
                            return;

                        var desiredIndex = PlaylistIndex + playOffsetMessage.Offset;
                        var index = IsLooping ? PlaylistIndex
                                  : IsShuffling ? Random.Shared.Next(0, ScriptPlaylist.Count)
                                  : desiredIndex < 0 ? 0
                                  : desiredIndex < ScriptPlaylist.Count ? desiredIndex
                                  : -1;

                        if (index < 0)
                            SetPlaylist(null);
                        else
                            PlayByIndex(index);
                    }
                }

                if (ScriptPlaylist == null)
                    continue;

                if (_scriptInfo == null)
                    PlayByIndex(IsShuffling ? Random.Shared.Next(0, ScriptPlaylist.Count) : 0);

                if (_position > _duration)
                {
                    if (IsLooping)
                        SetPosition(0);
                    else if (IsShuffling)
                        PlayByIndex(Random.Shared.Next(0, ScriptPlaylist.Count));
                    else if (PlaylistIndex < ScriptPlaylist.Count - 1)
                        PlayByIndex(PlaylistIndex + 1);
                    else
                        SetPlaylist(null);
                }

                if (!_isPlaying || _scriptInfo == null)
                    continue;

                SetPosition(_position + elapsed);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        SetPlaylist(null);
    }

    private void PlayByIndex(int index)
    {
        if (ScriptPlaylist == null)
            return;
        if (index < 0 || index >= ScriptPlaylist.Count)
            return;

        var scriptInfo = ScriptPlaylist[index];
        if (scriptInfo?.AsRefreshed().Exists == false)
            scriptInfo = null;

        if (_scriptInfo != scriptInfo)
        {
            var script = scriptInfo != null ? ScriptResource.FromFileInfo(scriptInfo, userLoaded: true) : null;
            if (script == null)
                return;

            PlaylistIndex = index;

            SetScriptInfo(scriptInfo);
            SetDuration(script?.Keyframes.Last().Position ?? double.NaN);
            SetPosition(script != null ? 0 : double.NaN, forceSeek: true);
        }
        else if (_scriptInfo != null)
        {
            SetPosition(0, forceSeek: true);
        }
    }

    private void PlayByName(string name)
    {
        if (ScriptPlaylist == null)
            return;

        var index = ScriptPlaylist.FindIndex(f => string.Equals(name, f.Name, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(name, f.FullName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        PlayByIndex(index);
    }

    public bool CanPlayNext => IsConnected && ScriptPlaylist != null;
    public bool CanPlayPrevious => IsConnected && ScriptPlaylist != null;
    public void PlayNext() => _ = _messageChannel.Writer.TryWrite(new PlayScriptWithOffsetMessage(1));
    public void PlayPrevious() => _ = _messageChannel.Writer.TryWrite(new PlayScriptWithOffsetMessage(-1));

    private void SetPlaylist(List<FileInfo> playlist)
    {
        ScriptPlaylist = playlist;

        SetScriptInfo(null);
        SetDuration(double.NaN);
        SetPosition(double.NaN);

        if (playlist == null)
            SetIsPlaying(false);

        while (_messageChannel.Reader.TryRead(out var _)) ;
    }

    private void SetScriptInfo(FileInfo scriptInfo)
    {
        static string GetFakeMediaPath(FileInfo scriptInfo)
        {
            const string mediaExtension = "mp4";
            var scriptPath = scriptInfo.FullName;
            var basePath = Path.ChangeExtension(scriptPath, null);
            var basePathExtension = Path.GetExtension(basePath);

            if (string.IsNullOrWhiteSpace(basePathExtension))
                return $"{basePath}.{mediaExtension}";

            foreach (var axis in DeviceAxis.All)
                foreach (var funscriptName in axis.FunscriptNames)
                    if (string.Equals(basePathExtension, $".{funscriptName}", StringComparison.InvariantCultureIgnoreCase))
                        return $"{Path.ChangeExtension(basePath, null)}.{mediaExtension}";

            return $"{basePath}.{mediaExtension}";
        }

        _eventAggregator.Publish(new MediaPathChangedMessage(scriptInfo != null ? GetFakeMediaPath(scriptInfo) : null));
        _scriptInfo = scriptInfo;
    }

    private void SetDuration(double duration)
    {
        _eventAggregator.Publish(new MediaDurationChangedMessage(double.IsFinite(duration) ? TimeSpan.FromSeconds(duration) : null));
        _duration = duration;
    }

    private void SetPosition(double position, bool forceSeek = false)
    {
        _eventAggregator.Publish(new MediaPositionChangedMessage(double.IsFinite(position) ? TimeSpan.FromSeconds(position) : null, forceSeek));
        _position = position;
    }

    private void SetIsPlaying(bool isPlaying)
    {
        _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying));
        _isPlaying = isPlaying;
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        var drop = e.Data.GetData(DataFormats.FileDrop);
        if (drop is not string[] paths)
            return;

        if (paths.Length == 1 && Path.GetExtension(paths[0]) == ".txt")
            paths = File.ReadAllLines(paths[0]).ToArray();

        var playlist = paths.Select(p => new FileInfo(p))
                            .Where(f => f.Exists && string.Equals(f.Extension, ".funscript", StringComparison.OrdinalIgnoreCase))
                            .ToList();

        if (playlist.Count > 0)
            _ = _messageChannel.Writer.TryWrite(new ScriptPlaylistMessage(playlist));
    }

    public void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.Link;
    }

    protected override void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            settings[nameof(IsShuffling)] = IsShuffling;
            settings[nameof(IsLooping)] = IsLooping;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(IsShuffling), out var isShuffling))
                IsShuffling = isShuffling;
            if (settings.TryGetValue<bool>(nameof(IsLooping), out var isLooping))
                IsLooping = isLooping;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(true);

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        void WhenConnected(Action callback)
        {
            if (Status == ConnectionStatus.Connected)
                callback();
        }

        #region IsShuffling
        s.RegisterAction($"{Name}::Shuffle::Set", b => b.WithSetting<bool>(s => s.WithLabel("Enable shuffle")).WithCallback((_, enabled) => IsShuffling = enabled));
        s.RegisterAction($"{Name}::Shuffle::Toggle", b => b.WithCallback(_ => IsShuffling = !IsShuffling));
        #endregion

        #region IsLooping
        s.RegisterAction($"{Name}::Looping::Set", b => b.WithSetting<bool>(s => s.WithLabel("Enable looping")).WithCallback((_, enabled) => IsLooping = enabled));
        s.RegisterAction($"{Name}::Looping::Toggle", b => b.WithCallback(_ => IsLooping = !IsLooping));
        #endregion

        #region Playlist
        s.RegisterAction($"{Name}::Playlist::Prev", b => b.WithCallback(_ => WhenConnected(PlayPrevious)));
        s.RegisterAction($"{Name}::Playlist::Next", b => b.WithCallback(_ => WhenConnected(PlayNext)));
        s.RegisterAction($"{Name}::Playlist::PlayByIndex", b => b.WithSetting<int>(s => s.WithLabel("Index")).WithCallback((_, index) => WhenConnected(() => PlayByIndex(index))));
        s.RegisterAction($"{Name}::Playlist::PlayByName", b => b.WithSetting<string>(s => s.WithLabel("File name/path")).WithCallback((_, name) => WhenConnected(() => PlayByName(name))));
        #endregion
    }

    public void Handle(MediaSeekMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            _messageChannel.Writer.TryWrite(message);
    }

    public void Handle(MediaPlayPauseMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            _messageChannel.Writer.TryWrite(message);
    }

    public void OnPlayScript(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not FileInfo scriptInfo)
            return;

        var playlist = ScriptPlaylist;
        if (playlist == null)
            return;

        var index = playlist.IndexOf(scriptInfo);
        if (index < 0)
            return;

        _ = _messageChannel.Writer.TryWrite(new PlayScriptAtIndexMessage(index));
    }

    public void OnIsLoopingChanged()
    {
        if (IsLooping && IsShuffling)
            IsShuffling = false;
    }

    public void OnIsShufflingChanged()
    {
        if (IsShuffling && IsLooping)
            IsLooping = false;
    }

    private record ScriptPlaylistMessage(List<FileInfo> Playlist);
    private record PlayScriptAtIndexMessage(int Index);
    private record PlayScriptWithOffsetMessage(int Offset);
}
