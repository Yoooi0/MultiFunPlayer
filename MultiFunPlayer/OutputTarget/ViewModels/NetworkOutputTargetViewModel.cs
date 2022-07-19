using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

public enum ProtocolType
{
    Tcp,
    Udp
}

[DisplayName("Network")]
public class NetworkOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);
    public ProtocolType Protocol { get; set; } = ProtocolType.Tcp;

    public NetworkOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider) { }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
    {
        if (Protocol == ProtocolType.Tcp)
            RunTcp(token);
        else if (Protocol == ProtocolType.Udp)
            RunUdp(token);
    }

    private void RunTcp(CancellationToken token)
    {
        using var client = new TcpClient();

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, $"tcp://{Endpoint}");
            client.Connect(Endpoint);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to server");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to server", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            using var stream = new StreamWriter(client.GetStream(), Encoding.UTF8);
            FixedUpdate(() => !token.IsCancellationRequested && client.Connected, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (client.Connected && client.Available > 0)
                {
                    var message = Encoding.UTF8.GetString(client.GetStream().ReadBytes(client.Available));
                    Logger.Debug("Received \"{0}\" from \"{1}\"", message, $"tcp://{Endpoint}");
                }

                var commands = DeviceAxis.ToString(Values, elapsed * 1000);
                if (client.Connected && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"tcp://{Endpoint}");
                    stream.WriteLine(commands);
                }
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
    }

    private void RunUdp(CancellationToken token)
    {
        using var client = new UdpClient();

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, $"udp://{Endpoint}");

            const int SIO_UDP_CONNRESET = -1744830452;
            client.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

            client.Connect(Endpoint);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to server");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to server", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var buffer = new byte[256];
            FixedUpdate(() => !token.IsCancellationRequested, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (client.Available > 0)
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var message = Encoding.UTF8.GetString(client.Receive(ref endpoint));
                    Logger.Debug("Received \"{0}\" from \"{1}\"", message, $"udp://{endpoint}");
                }

                var commands = DeviceAxis.ToString(Values, elapsed * 1000);
                if (!string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"udp://{Endpoint}");

                    var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                    client.Send(buffer, encoded);
                }
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Endpoint)] = Endpoint?.ToString();
            settings[nameof(Protocol)] = Protocol.ToString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
            if (settings.TryGetValue<ProtocolType>(nameof(Protocol), out var protocol))
                Protocol = protocol;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction($"{Identifier}::Endpoint::Set", b => b.WithSetting<string>(s => s.WithLabel("Endpoint").WithDescription("ip/host:port")).WithCallback((_, endpointString) =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        }));
        #endregion

        #region Protocol
        s.RegisterAction($"{Identifier}::Protocol::Set", b => b.WithSetting<ProtocolType?>(s => s.WithLabel("Protocol").WithItemsSource(EnumUtils.GetValues<ProtocolType?>())).WithCallback((_, protocol) =>
        {
            if (protocol.HasValue)
                Protocol = protocol.Value;
        }));
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::Endpoint::Set");
        s.UnregisterAction($"{Identifier}::Protocol::Set");
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            if (Protocol == ProtocolType.Udp)
                return await ValueTask.FromResult(true);

            using var client = new TcpClient();
            client.Connect(Endpoint);
            client.GetStream();
            return await ValueTask.FromResult(true);
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }
}
