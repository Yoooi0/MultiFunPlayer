using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("OFS")]
internal class OfsMediaSourceViewModel : AbstractMediaSource, IHandle<MediaPlayPauseMessage>, IHandle<MediaSeekMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Channel<object> _writeMessageChannel;

    public override ConnectionStatus Status { get; protected set; }

    public Uri Uri { get; set; } = new Uri("ws://127.0.0.1:8080/ofs");

    public OfsMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
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
            using var client = new ClientWebSocket();

            Logger.Info("Connecting to {0} at \"{1}\"", Name, Uri.ToString());
            await client.ConnectAsync(Uri, token)
                        .WithCancellation(1000);

            Status = ConnectionStatus.Connected;

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(ReadAsync(client, cancellationSource.Token), WriteAsync(client, cancellationSource.Token));
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

        EventAggregator.Publish(new MediaPathChangedMessage(null));
        EventAggregator.Publish(new MediaPlayingChangedMessage(false));
    }

    private async Task ReadAsync(ClientWebSocket client, CancellationToken token)
    {
        try
        {
            var readBuffer = new byte[1024];
            while (!token.IsCancellationRequested && client.State == WebSocketState.Open)
            {
                using var memory = new MemoryStream();

                var result = default(WebSocketReceiveResult);
                do
                {
                    result = await client.ReceiveAsync(readBuffer, token);
                    memory.Write(readBuffer, 0, result.Count);
                } while (!token.IsCancellationRequested && !result.EndOfMessage);

                var message = Encoding.UTF8.GetString(memory.ToArray());
                if (message == null)
                    continue;

                try
                {
                    Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);

                    var document = JObject.Parse(message);
                    if (!document.TryGetValue<string>("type", out var type) || !string.Equals(type, "event", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!document.TryGetValue<string>("name", out var eventName) || !document.TryGetObject(out var dataToken, "data"))
                        continue;

                    switch (eventName)
                    {
                        case "media_change":
                                EventAggregator.Publish(new MediaPathChangedMessage(dataToken.TryGetValue<string>("path", out var path) && !string.IsNullOrWhiteSpace(path) ? path : null, ReloadScripts: false));
                            break;
                        case "play_change":
                            if (dataToken.TryGetValue<bool>("playing", out var isPlaying))
                                EventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying));
                            break;
                        case "duration_change":
                            if (dataToken.TryGetValue<double>("duration", out var duration) && duration >= 0)
                                EventAggregator.Publish(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                            break;
                        case "time_change":
                            if (dataToken.TryGetValue<double>("time", out var position) && position >= 0)
                                EventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
                            break;
                        case "playbackspeed_change":
                            if (dataToken.TryGetValue<double>("speed", out var speed) && speed > 0)
                                EventAggregator.Publish(new MediaSpeedChangedMessage(speed));
                            break;
                        case "funscript_change":
                            {
                                if (!dataToken.TryGetObject(out var funscriptToken, "funscript"))
                                    break;
                                if (!dataToken.TryGetValue<string>("name", out var name) || string.IsNullOrWhiteSpace(name))
                                    break;

                                var axes = DeviceAxisUtils.FindAxesMatchingName(name);
                                if (!axes.Any())
                                    break;

                                var script = FunscriptReader.Default.FromBytes(name, Uri.ToString(), Encoding.UTF8.GetBytes(funscriptToken.ToString()));
                                EventAggregator.Publish(new ScriptChangedMessage(axes.ToDictionary(a => a, _ => script)));
                            }

                            break;
                        case "funscript_remove":
                            {
                                if (!dataToken.TryGetValue<string>("name", out var name) || string.IsNullOrWhiteSpace(name))
                                    break;

                                var axes = DeviceAxisUtils.FindAxesMatchingName(name);
                                if (!axes.Any())
                                    break;

                                EventAggregator.Publish(new ScriptChangedMessage(axes.ToDictionary(a => a, _ => default(IScriptResource))));
                            }

                            break;
                        case "project_change":
                            EventAggregator.Publish(new MediaPathChangedMessage(null, ReloadScripts: false));
                            EventAggregator.Publish(new MediaPlayingChangedMessage(false));
                            break;
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync(ClientWebSocket client, CancellationToken token)
    {
        static string CreateMessage(string commandName, string dataName, string dataValue)
            => @$"{{ ""type"": ""command"", ""name"": ""{commandName}"", ""data"": {{ ""{dataName}"": {dataValue} }} }}";

        try
        {
            while (!token.IsCancellationRequested && client.State == WebSocketState.Open)
            {
                await _writeMessageChannel.Reader.WaitToReadAsync(token);
                var message = await _writeMessageChannel.Reader.ReadAsync(token);

                var messageString = message switch
                {
                    MediaPlayPauseMessage playPauseMessage => CreateMessage("change_play", "playing", playPauseMessage.State.ToString().ToLower()),
                    MediaSeekMessage seekMessage when seekMessage.Position.HasValue => CreateMessage("change_time", "time", seekMessage.Position.Value.TotalSeconds.ToString("F4").Replace(',', '.')),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(messageString))
                    continue;

                Logger.Trace("Sending \"{0}\" to \"{1}\"", messageString, Name);
                await client.SendAsync(Encoding.UTF8.GetBytes(messageString), WebSocketMessageType.Text, true, token);
            }
        }
        catch (OperationCanceledException) { }
    }
    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Uri)] = Uri?.ToString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<Uri>(nameof(Uri), out var uri))
                Uri = uri;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        if (Uri == null)
            return false;

        try
        {
            using var client = new ClientWebSocket();
            await client.ConnectAsync(Uri, token);

            var result = client.State == WebSocketState.Open;
            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
            return result;
        }
        catch
        {
            return false;
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Uri
        s.RegisterAction<string>($"{Name}::Uri::Set", s => s.WithLabel("Uri").WithDescription("ofs websocket uri"), uriString =>
        {
            if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                Uri = uri;
        });
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
}
