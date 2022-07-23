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
    private readonly Channel<IScriptResource> _scriptChannel;
    private readonly Channel<object> _messageChannel;

    private bool _isPlaying;
    private double _currentPosition;

    public override ConnectionStatus Status { get; protected set; }

    public InternalMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _scriptChannel = Channel.CreateUnbounded<IScriptResource>(new UnboundedChannelOptions()
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

            var currentScript = default(IScriptResource);
            var lastTicks = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                if (_scriptChannel.Reader.TryRead(out var script))
                {
                    _currentPosition = 0;
                    _eventAggregator.Publish(new MediaDurationChangedMessage(TimeSpan.FromSeconds(script.Keyframes.Last().Position)));
                    _eventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(0)));
                    _eventAggregator.Publish(new ScriptLoadMessage(DeviceAxis.All.First(), script));

                    if (currentScript == null)
                    {
                        _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: true));
                        _isPlaying = true;
                    }

                    currentScript = script;
                }

                while (_messageChannel.Reader.TryRead(out var message))
                {
                    if (message is MediaPlayPauseMessage playPauseMessage)
                    {
                        _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: playPauseMessage.State));
                        _isPlaying = playPauseMessage.State;
                    }
                    else if (message is MediaSeekMessage seekMessage && seekMessage.Position.HasValue)
                    {
                        _currentPosition = seekMessage.Position.Value.TotalSeconds;
                    }
                }

                var currentTicks = Stopwatch.GetTimestamp();
                var elapsed = (currentTicks - lastTicks) / (double)Stopwatch.Frequency;
                lastTicks = currentTicks;

                if (!_isPlaying)
                    continue;

                _currentPosition += elapsed;
                _eventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(_currentPosition)));
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

        _isPlaying = false;
        _currentPosition = 0;
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        var drop = e.Data.GetData(DataFormats.FileDrop);
        if (drop is not IEnumerable<string> paths)
            return;

        var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
        if (path == null)
            return;

        _ = _scriptChannel.Writer.TryWrite(ScriptResource.FromPath(path, userLoaded: true));
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
}
