﻿using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Internal")]
internal class InternalMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>, IHandle<MediaChangePathMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _playlistLock = new();
    private readonly Channel<object> _messageChannel;

    private bool _isPlaying;
    private double _position;
    private double _duration;
    private FileInfo _scriptInfo;

    public override ConnectionStatus Status { get; protected set; }
    public int PlaylistIndex { get; set; } = 0;
    public Playlist ScriptPlaylist { get; set; } = null;

    public bool IsShuffling { get; set; } = false;
    public bool IsLooping { get; set; } = false;

    public InternalMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
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
            SetScriptInfo(null);
            SetDuration(double.NaN);
            SetPosition(double.NaN);
            SetIsPlaying(false);

            while (_messageChannel.Reader.TryRead(out var _)) ;

            Status = ConnectionStatus.Connected;

            var lastTicks = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                lock (_playlistLock)
                {
                    var currentTicks = Stopwatch.GetTimestamp();
                    var elapsed = (currentTicks - lastTicks) / (double)Stopwatch.Frequency;
                    lastTicks = currentTicks;

                    while (_messageChannel.Reader.TryRead(out var message))
                    {
                        if (message is MediaPlayPauseMessage playPauseMessage) { SetIsPlaying(playPauseMessage.ShouldBePlaying); }
                        else if (message is MediaSeekMessage seekMessage && _scriptInfo != null) { SetPosition(seekMessage.Position?.TotalSeconds ?? double.NaN); }
                        else if (message is MediaChangePathMessage changePathMessage)
                        {
                            var path = changePathMessage.Path;
                            var playlistIndex = ScriptPlaylist?.FindIndex(path);

                            if (playlistIndex >= 0)
                                PlayByIndex(playlistIndex.Value);
                            else
                                SetPlaylist(CreatePlaylist(path));
                        }
                        else if (message is PlayScriptAtIndexMessage playIndexMessage) { PlayByIndex(playIndexMessage.Index); }
                        else if (message is PlayScriptWithOffsetMessage playOffsetMessage)
                        {
                            if (ScriptPlaylist == null)
                                continue;

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
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        if (IsDisposing)
            return;

        SetScriptInfo(null);
        SetDuration(double.NaN);
        SetPosition(double.NaN);
        SetIsPlaying(false);

        while (_messageChannel.Reader.TryRead(out var _));
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
            var script = scriptInfo != null ? FunscriptReader.Default.FromFileInfo(scriptInfo) : null;
            if (script == null)
                return;

            PlaylistIndex = index;

            SetScriptInfo(scriptInfo);
            SetDuration(script?.Keyframes?[^1].Position ?? double.NaN);
            SetPosition(script != null ? 0 : double.NaN, forceSeek: true);
        }
        else if (_scriptInfo != null)
        {
            SetPosition(0, forceSeek: true);
        }
    }

    public bool CanPlayNext => IsConnected && ScriptPlaylist != null;
    public bool CanPlayPrevious => IsConnected && ScriptPlaylist != null;
    public void PlayNext() => _ = _messageChannel.Writer.TryWrite(new PlayScriptWithOffsetMessage(1));
    public void PlayPrevious() => _ = _messageChannel.Writer.TryWrite(new PlayScriptWithOffsetMessage(-1));
    public bool CanClearPlaylist => IsConnected && ScriptPlaylist != null;
    public void ClearPlaylist() => SetPlaylist(null);

    private Playlist CreatePlaylist(params string[] paths)
    {
        if (paths == null)
            return null;

        var isPlaylistFile = paths.Length == 1 && Path.GetExtension(paths[0]) == ".txt";
        return isPlaylistFile ? new Playlist(paths[0]) : new Playlist(paths);
    }

    private void SetPlaylist(Playlist playlist)
    {
        lock (_playlistLock)
        {
            ScriptPlaylist = playlist;
            PlaylistIndex = 0;

            SetScriptInfo(null);
            SetDuration(double.NaN);
            SetPosition(double.NaN);

            if (playlist == null)
                SetIsPlaying(false);
        }
    }

    private void SetScriptInfo(FileInfo scriptInfo)
    {
        _scriptInfo = scriptInfo;
        if (Status != ConnectionStatus.Connected && Status != ConnectionStatus.Disconnecting)
            return;

        EventAggregator.Publish(new ChangeScriptMessage(DeviceAxis.All, null));

        if (scriptInfo != null)
        {
            var axes = DeviceAxisUtils.FindAxesMatchingName(scriptInfo.Name, true);
            EventAggregator.Publish(new ChangeScriptMessage(axes, FunscriptReader.Default.FromFileInfo(scriptInfo)));
        }
    }

    private void SetDuration(double duration)
    {
        _duration = duration;
        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Disconnecting)
            EventAggregator.Publish(new MediaDurationChangedMessage(double.IsFinite(duration) ? TimeSpan.FromSeconds(duration) : null));
    }

    private void SetPosition(double position, bool forceSeek = false)
    {
        _position = position;
        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Disconnecting)
            EventAggregator.Publish(new MediaPositionChangedMessage(double.IsFinite(position) ? TimeSpan.FromSeconds(position) : null, forceSeek));
    }

    private void SetIsPlaying(bool isPlaying)
    {
        _isPlaying = isPlaying;
        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Disconnecting)
            EventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying));
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        var drop = e.Data.GetData(DataFormats.FileDrop);
        if (drop is not string[] paths)
            return;

        SetPlaylist(CreatePlaylist(paths));
    }

    public void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.Link;
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(IsShuffling)] = IsShuffling;
            settings[nameof(IsLooping)] = IsLooping;

            settings[nameof(ScriptPlaylist)] = ScriptPlaylist switch
            {
                Playlist { SourceFile: not null } => JToken.FromObject(ScriptPlaylist.SourceFile.FullName),
                Playlist { Count: > 0 } => JArray.FromObject(ScriptPlaylist),
                _ => null,
            };
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(IsShuffling), out var isShuffling))
                IsShuffling = isShuffling;
            if (settings.TryGetValue<bool>(nameof(IsLooping), out var isLooping))
                IsLooping = isLooping;

            if (settings.TryGetValue(nameof(ScriptPlaylist), out var scriptPlaylistToken))
            {
                if (scriptPlaylistToken.Type == JTokenType.String && scriptPlaylistToken.TryToObject<string>(out var playlistFile) && Path.GetExtension(playlistFile) == ".txt")
                    SetPlaylist(new Playlist(playlistFile));
                else if (scriptPlaylistToken.Type == JTokenType.Array && scriptPlaylistToken.TryToObject<List<string>>(out var playlistFiles))
                    SetPlaylist(new Playlist(playlistFiles));
            }
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(true);

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        void WhenConnected(Action callback)
        {
            if (Status == ConnectionStatus.Connected)
                callback();
        }

        #region IsShuffling
        s.RegisterAction<bool>($"{Name}::Shuffle::Set", s => s.WithLabel("Enable shuffle"), enabled => IsShuffling = enabled);
        s.RegisterAction($"{Name}::Shuffle::Toggle", () => IsShuffling = !IsShuffling);
        #endregion

        #region IsLooping
        s.RegisterAction<bool>($"{Name}::Looping::Set", s => s.WithLabel("Enable looping"), enabled => IsLooping = enabled);
        s.RegisterAction($"{Name}::Looping::Toggle", () => IsLooping = !IsLooping);
        #endregion

        #region Playlist
        s.RegisterAction($"{Name}::Playlist::Clear", () => WhenConnected(ClearPlaylist));
        s.RegisterAction($"{Name}::Playlist::Prev", () => WhenConnected(PlayPrevious));
        s.RegisterAction($"{Name}::Playlist::Next", () => WhenConnected(PlayNext));
        s.RegisterAction<int>($"{Name}::Playlist::PlayByIndex", s => s.WithLabel("Index"), index => WhenConnected(() => _messageChannel.Writer.TryWrite(new PlayScriptAtIndexMessage(index))));
        s.RegisterAction<string>($"{Name}::Playlist::PlayByName", s => s.WithLabel("File name/path"), name => WhenConnected(() =>
        {
            var playlist = ScriptPlaylist;
            if (playlist == null)
                return;

            var index = playlist.FindIndex(name);
            if (index >= 0)
                _messageChannel.Writer.TryWrite(new PlayScriptAtIndexMessage(index));
        }));
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

    public void Handle(MediaChangePathMessage message)
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

        var index = playlist.FindIndex(scriptInfo);
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

    private record PlayScriptAtIndexMessage(int Index);
    private record PlayScriptWithOffsetMessage(int Offset);

    internal class Playlist : IReadOnlyList<FileInfo>
    {
        private readonly List<FileInfo> _files;

        public FileInfo SourceFile { get; }

        public Playlist(string sourceFile)
        {
            SourceFile = new FileInfo(sourceFile);

            if (SourceFile.Exists)
                _files = File.ReadAllLines(SourceFile.FullName)
                             .Select(p => new FileInfo(p))
                             .Where(f => f.Exists && string.Equals(f.Extension, ".funscript", StringComparison.OrdinalIgnoreCase))
                             .ToList();

            _files ??= new List<FileInfo>();
        }

        public Playlist(IEnumerable<string> files)
        {
            _files = files.Select(p => new FileInfo(p))
                          .Where(f => f.Exists && string.Equals(f.Extension, ".funscript", StringComparison.OrdinalIgnoreCase))
                          .ToList();
        }

        public FileInfo this[int index] => _files[index];
        public int Count => _files.Count;
        public IEnumerator<FileInfo> GetEnumerator() => _files.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _files.GetEnumerator();

        public int FindIndex(Predicate<FileInfo> match) => _files.FindIndex(match);
        public int FindIndex(FileInfo file) => FindIndex(f => string.Equals(f.Name, file.Name, StringComparison.OrdinalIgnoreCase)
                                                           || string.Equals(f.FullName, file.FullName, StringComparison.OrdinalIgnoreCase));
        public int FindIndex(string path) => FindIndex(f => string.Equals(f.Name, path, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase));
    }
}