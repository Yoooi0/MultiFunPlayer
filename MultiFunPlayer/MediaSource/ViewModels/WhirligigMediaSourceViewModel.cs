using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.MediaSource.ViewModels;

[DisplayName("Whirligig")]
public class WhirligigMediaSourceViewModel : AbstractMediaSource
{
    private Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;

    public override ConnectionStatus Status { get; protected set; }

    public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 2000);

    public WhirligigMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator)
        : base(shortcutManager, eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, Endpoint);

            if (string.Equals(Endpoint.Address.ToString(), "localhost") || string.Equals(Endpoint.Address.ToString(), "127.0.0.1"))
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)whirligig")))
                    throw new Exception($"Could not find a running {Name} process.");

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(5000);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);

            Status = ConnectionStatus.Connected;
            while (!token.IsCancellationRequested && client.Connected && !reader.EndOfStream)
            {
                var message = await reader.ReadLineAsync().WithCancellation(token);
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);
                if (message.Length >= 1 && message[0] == 'C')
                {
                    var parts = message.Split(' ', 2);
                    _eventAggregator.Publish(new MediaPathChangedMessage(parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim('"') : null));
                }
                else if (message.Length >= 1 && message[0] == 'S')
                {
                    _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: false));
                }
                else if (message.Length >= 8 && message[..8] == "duration")
                {
                    var parts = message.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var duration) && duration >= 0)
                        _eventAggregator.Publish(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                }
                else if (message.Length >= 1 && message[0] == 'P')
                {
                    var parts = message.Split(' ', 2);
                    _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: true));

                    if (parts.Length == 2 && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position) && position >= 0)
                        _eventAggregator.Publish(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException e) { Logger.Debug(e, $"{Name} failed with exception"); }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Name} failed with exception", "RootDialog");
        }

        _eventAggregator.Publish(new MediaPathChangedMessage(null));
        _eventAggregator.Publish(new MediaPlayingChangedMessage(isPlaying: false));
    }

    protected override void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            if (Endpoint != null)
                settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<IPEndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (Endpoint == null)
                return await ValueTask.FromResult(false);

            if (string.Equals(Endpoint.Address.ToString(), "localhost") || string.Equals(Endpoint.Address.ToString(), "127.0.0.1"))
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)whirligig")))
                    return await ValueTask.FromResult(false);

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(2500);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint.Address, Endpoint.Port, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();

            return await ValueTask.FromResult(client.Connected);
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        #region Endpoint
        s.RegisterAction($"{Name}::Endpoint::Set", b => b.WithSetting<string>(s => s.WithLabel("Endpoint").WithDescription("ip:port")).WithCallback((_, endpointString) =>
        {
            if (IPEndPoint.TryParse(endpointString, out var endpoint))
                Endpoint = endpoint;
        }));
        #endregion
    }
}
