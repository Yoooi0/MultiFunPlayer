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
            Logger.Info("Connecting to {0} at \"{1}\"", Name);

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
                    else if (message is PlayScriptAtIndexMessage playIndexMessage) { SetScriptByPlaylistIndex(playIndexMessage.Index); }
                    else if (message is PlayPrevNextScriptMessage prevNextMessage)
                    {
                        var desiredIndex = PlaylistIndex + prevNextMessage.Offset;
                        var index = IsLooping ? PlaylistIndex
                                  : IsShuffling ? Random.Shared.Next(0, ScriptPlaylist.Count)
                                  : desiredIndex < 0 ? 0
                                  : desiredIndex < ScriptPlaylist.Count ? desiredIndex
                                  : -1;

                        if (index < 0)
                            SetPlaylist(null);
                        else
                            SetScriptByPlaylistIndex(index);
                    }
                }

                if (ScriptPlaylist == null)
                    continue;

                if (_scriptInfo == null)
                    SetScriptByPlaylistIndex(IsShuffling ? Random.Shared.Next(0, ScriptPlaylist.Count) : 0);

                if (_position > _duration)
                {
                    if (IsLooping)
                        SetPosition(0);
                    else if (IsShuffling)
                        SetScriptByPlaylistIndex(Random.Shared.Next(0, ScriptPlaylist.Count));
                    else if (PlaylistIndex < ScriptPlaylist.Count - 1)
                        SetScriptByPlaylistIndex(PlaylistIndex + 1);
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

    private void SetPlaylist(List<FileInfo> playlist)
    {
        ScriptPlaylist = playlist;
        SetScript(null);

        if (playlist == null)
            SetIsPlaying(false);
    }

    private void SetScriptByPlaylistIndex(int index)
    {
        if (index < 0 || index >= ScriptPlaylist.Count)
            return;

        PlaylistIndex = index;
        SetScript(ScriptPlaylist[PlaylistIndex]);
    }

    private void SetScript(FileInfo scriptInfo)
    {
        if (scriptInfo?.AsRefreshed().Exists == false)
            scriptInfo = null;

        if (_scriptInfo != scriptInfo)
        {
            _scriptInfo = scriptInfo;
            var script = scriptInfo != null ? ScriptResource.FromFileInfo(scriptInfo, userLoaded: true) : null;

            _eventAggregator.Publish(new MediaPathChangedMessage(GetFakeMediaPath(scriptInfo)));
            SetDuration(script?.Keyframes.Last().Position ?? double.NaN);
            SetPosition(script != null ? 0 : double.NaN, forceSeek: true);
        }
        else if(_scriptInfo != null)
        {
            SetPosition(0, forceSeek: true);
        }
    }

    private string GetFakeMediaPath(FileInfo scriptInfo)
    {
        if (scriptInfo == null)
            return null;

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

    public bool CanPlayNext => IsConnected && ScriptPlaylist != null;
    public bool CanPlayPrevious => IsConnected && ScriptPlaylist != null;
    public void PlayNext() => _ = _messageChannel.Writer.TryWrite(new PlayPrevNextScriptMessage(1));
    public void PlayPrevious() => _ = _messageChannel.Writer.TryWrite(new PlayPrevNextScriptMessage(-1));

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
    private record PlayPrevNextScriptMessage(int Offset);
}
