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
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);
    public ProtocolType Protocol { get; set; } = ProtocolType.Tcp;

    public NetworkOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(shortcutManager, eventAggregator, valueProvider) { }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
    {
        if (Endpoint == null)
            return;

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
            Logger.Info("Connecting to {0}", $"tcp://{Endpoint}");
            client.Connect(Endpoint);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to server");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Error when connecting to server:\n\n{e}"), "RootDialog"));
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var stopwatch = Stopwatch.StartNew();
            using var stream = new StreamWriter(client.GetStream(), Encoding.ASCII);
            while (!token.IsCancellationRequested && client?.Connected == true)
            {
                UpdateValues();

                var commands = DeviceAxis.ToString(Values, UpdateInterval);
                if (client.Connected && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"tcp://{Endpoint}");
                    stream.WriteLine(commands);
                }

                Sleep(stopwatch);
                stopwatch.Restart();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Unhandled error");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Unhandled error:\n\n{e}"), "RootDialog"));
        }
    }

    private void RunUdp(CancellationToken token)
    {
        using var client = new UdpClient();

        try
        {
            Logger.Info("Connecting to {0}", $"udp://{Endpoint}");
            client.Connect(Endpoint);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to server");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Error when connecting to server:\n\n{e}"), "RootDialog"));
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var buffer = new byte[256];
            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                UpdateValues();

                var commands = DeviceAxis.ToString(Values, UpdateInterval);
                if (!string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"udp://{Endpoint}");

                    var encoded = Encoding.ASCII.GetBytes(commands, buffer);
                    client.Send(buffer, encoded);
                }

                Sleep(stopwatch);
                stopwatch.Restart();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog"));
        }
    }

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        base.HandleSettings(settings, type);

        if (type == AppSettingsMessageType.Saving)
        {
            if (Endpoint != null)
                settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());

            settings[nameof(Protocol)] = new JValue(Protocol.ToString());
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<IPEndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;

            if (settings.TryGetValue<ProtocolType>(nameof(Protocol), out var protocol))
                Protocol = protocol;
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

        #region Protocol
        s.RegisterAction($"{Name}::Protocol::Set", b => b.WithSetting<ProtocolType?>(s => s.WithLabel("Protocol").WithItemsSource(EnumUtils.GetValues<ProtocolType?>())).WithCallback((_, protocol) =>
        {
            if (protocol.HasValue)
                Protocol = protocol.Value;
        }));
        #endregion
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
