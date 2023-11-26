using MultiFunPlayer.Common;
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
internal sealed class WhirligigMediaSourceViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 2000);

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, Endpoint);

            if (Endpoint.IsLocalhost())
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)whirligig")))
                    throw new Exception($"Could not find a running {Name} process.");

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(5000);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);

            Status = ConnectionStatus.Connected;
            while (!token.IsCancellationRequested && client.Connected && !reader.EndOfStream)
            {
                var message = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                Logger.Trace("Received \"{0}\" from \"{1}\"", message, Name);
                if (message.Length >= 1 && message[0] == 'C')
                {
                    var parts = message.Split(' ', 2);
                    PublishMessage(new MediaPathChangedMessage(parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim('"') : null));
                }
                else if (message.Length >= 1 && message[0] == 'S')
                {
                    PublishMessage(new MediaPlayingChangedMessage(false));
                }
                else if (message.Length >= 8 && message[..8] == "duration")
                {
                    var parts = message.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var duration) && duration >= 0)
                        PublishMessage(new MediaDurationChangedMessage(TimeSpan.FromSeconds(duration)));
                }
                else if (message.Length >= 1 && message[0] == 'P')
                {
                    var parts = message.Split(' ', 2);
                    PublishMessage(new MediaPlayingChangedMessage(true));

                    if (parts.Length == 2 && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, NumberFormatInfo.InvariantInfo, out var position) && position >= 0)
                        PublishMessage(new MediaPositionChangedMessage(TimeSpan.FromSeconds(position)));
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

        if (IsDisposing)
            return;

        PublishMessage(new MediaPathChangedMessage(null));
        PublishMessage(new MediaPlayingChangedMessage(false));
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Endpoint)] = Endpoint?.ToString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (Endpoint == null)
                return false;

            if (Endpoint.IsLocalhost())
                if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)whirligig")))
                    return false;

            using var client = new TcpClient();
            {
                using var timeoutCancellationSource = new CancellationTokenSource(2500);
                using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCancellationSource.Token);

                await client.ConnectAsync(Endpoint, connectCancellationSource.Token);
            }

            using var stream = client.GetStream();

            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Name}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ip/host:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        });
        #endregion
    }
}
