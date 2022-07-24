using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
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
    private readonly Channel<List<string>> _scriptQueueChannel;
    private readonly Channel<object> _messageChannel;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsShuffling { get; set; } = false;
    public bool IsLooping { get; set; } = false;

    public InternalMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _scriptQueueChannel = Channel.CreateUnbounded<List<string>>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });
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

            _eventAggregator.Publish(new ScriptLoadMessage(DeviceAxis.All.First(), null));
            _eventAggregator.Publish(new MediaPathChangedMessage(null));
            _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: false));
            _eventAggregator.Publish(new MediaDurationChangedMessage(null));

            Status = ConnectionStatus.Connected;

            var isPlaying = false;
            var currentPosition = double.NaN;
            var currentDuration = double.NaN;
            var currentScriptPath = default(string);
            var currentQueueIndex = 0;
            var scriptQueue = default(List<string>);
            var lastTicks = Stopwatch.GetTimestamp();

            void SetCurrentQueue(List<string> queue)
            {
                scriptQueue = queue;
                SetCurrentScript(null);

                if (queue == null)
                    SetIsPlaying(false);
            }

            void SetCurrentScriptIndex(int index)
            {
                currentQueueIndex = MathUtils.Clamp(index, 0, scriptQueue.Count - 1);
                SetCurrentScript(scriptQueue[currentQueueIndex]);
            }

            void SetCurrentScript(string scriptPath)
            {
                static string GetMediaPath(string scriptPath)
                {
                    if (scriptPath == null)
                        return null;

                    const string mediaExtension = "mp4";
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

                var script = scriptPath != null ? ScriptResource.FromPath(scriptPath, userLoaded: true) : null;
                SetCurrentDuration(script?.Keyframes.Last().Position ?? double.NaN);
                SetCurrentPosition(script != null ? 0 : double.NaN);

                currentScriptPath = scriptPath;
                _eventAggregator.Publish(new MediaPathChangedMessage(GetMediaPath(scriptPath)));
            }

            void SetCurrentDuration(double duration)
            {
                _eventAggregator.Publish(new MediaDurationChangedMessage(double.IsFinite(duration) ? TimeSpan.FromSeconds(duration) : null));
                currentDuration = duration;
            }

            void SetCurrentPosition(double position)
            {
                _eventAggregator.Publish(new MediaPositionChangedMessage(double.IsFinite(position) ? TimeSpan.FromSeconds(position) : null));
                currentPosition = position;
            }

            void SetIsPlaying(bool state)
            {
                _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: state));
                isPlaying = state;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                var currentTicks = Stopwatch.GetTimestamp();
                var elapsed = (currentTicks - lastTicks) / (double)Stopwatch.Frequency;
                lastTicks = currentTicks;

                while (_messageChannel.Reader.TryRead(out var message))
                {
                    if (message is MediaPlayPauseMessage playPauseMessage)
                        SetIsPlaying(playPauseMessage.State);
                    else if (message is MediaSeekMessage seekMessage && seekMessage.Position.HasValue && currentScriptPath != null)
                        SetCurrentPosition(seekMessage.Position.Value.TotalSeconds);
                }

                if (_scriptQueueChannel.Reader.TryRead(out var queue))
                    SetCurrentQueue(queue);
                else if (scriptQueue == null)
                    continue;

                if (currentScriptPath == null)
                    SetCurrentScriptIndex(IsShuffling ? Random.Shared.Next(0, scriptQueue.Count) : 0);

                if (currentPosition > currentDuration)
                {
                    if (IsLooping)
                        SetCurrentPosition(0);
                    else if (IsShuffling)
                        SetCurrentScriptIndex(Random.Shared.Next(0, scriptQueue.Count));
                    else if (currentQueueIndex < scriptQueue.Count - 1)
                        SetCurrentScriptIndex(currentQueueIndex + 1);
                    else
                        SetCurrentQueue(null);
                }

                if (!isPlaying || currentScriptPath == null)
                    continue;

                SetCurrentPosition(currentPosition + elapsed);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        _eventAggregator.Publish(new ScriptLoadMessage(DeviceAxis.All.First(), null));
        _eventAggregator.Publish(new MediaPathChangedMessage(null));
        _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: false));
        _eventAggregator.Publish(new MediaDurationChangedMessage(null));
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        var drop = e.Data.GetData(DataFormats.FileDrop);
        if (drop is not string[] paths)
            return;

        if (paths.Length == 1 && Path.GetExtension(paths[0]) == ".txt")
            paths = File.ReadAllLines(paths[0]).ToArray();

        var result = paths.Where(p => File.Exists(p) && Path.GetExtension(p) == ".funscript").ToList();
        if (result.Count > 0)
            _ = _scriptQueueChannel.Writer.TryWrite(result);
    }

    public void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.Link;
    }

    protected override void HandleSettings(JObject settings, SettingsAction action) { }
    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(true);

    public async void Handle(MediaSeekMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _messageChannel.Writer.WriteAsync(message);
    }

    public async void Handle(MediaPlayPauseMessage message)
    {
        if (Status == ConnectionStatus.Connected)
            await _messageChannel.Writer.WriteAsync(message);
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
}
