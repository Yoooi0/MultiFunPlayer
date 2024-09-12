using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
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
internal sealed class WhirligigMediaSource(IShortcutManager shortcutManager, IEventAggregator eventAggregator) : AbstractMediaSource(shortcutManager, eventAggregator)
{
    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 2000);

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Name, Endpoint?.ToUriString(), connectionType);

        if (Endpoint == null)
            throw new MediaSourceException("Endpoint cannot be null");
        if (Endpoint.IsLocalhost())
            if (!Process.GetProcesses().Any(p => Regex.IsMatch(p.ProcessName, "(?i)whirligig")))
                throw new MediaSourceException($"Could not find a running {Name} process");

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        using var client = new TcpClient();

        try
        {
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (connectionType == ConnectionType.AutoConnect)
                cancellationSource.CancelAfter(500);

            await client.ConnectAsync(Endpoint, cancellationSource.Token);

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0} at \"{1}\"", Name, Endpoint?.ToUriString());
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
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
            settings[nameof(Endpoint)] = Endpoint?.ToUriString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
        }
    }

    protected override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Name}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ipOrHost:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        });
        #endregion
    }
}
