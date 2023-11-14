using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("TCP")]
internal class TcpOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : ThreadAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public bool OffloadElapsedTime { get; set; } = true;
    public bool SendDirtyValuesOnly { get; set; } = true;
    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
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
            Logger.Error(e, "Error when connecting to server");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to server", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var buffer = new byte[256];
            var stream = client.GetStream();
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested && client.Connected, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (client.Connected && client.Available > 0)
                {
                    var message = Encoding.UTF8.GetString(stream.ReadBytes(client.Available));
                    Logger.Debug("Received \"{0}\" from \"{1}\"", message, $"tcp://{Endpoint}");
                }

                var values = SendDirtyValuesOnly ? Values.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : Values;
                values = values.Where(x => AxisSettings[x.Key].Enabled);

                var commands = OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                if (client.Connected && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"tcp://{Endpoint}");

                    var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                    stream.Write(buffer, 0, encoded);
                    lastSentValues.Merge(values);
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
            settings[nameof(OffloadElapsedTime)] = OffloadElapsedTime;
            settings[nameof(SendDirtyValuesOnly)] = SendDirtyValuesOnly;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
            if (settings.TryGetValue<bool>(nameof(OffloadElapsedTime), out var offloadElapsedTime))
                OffloadElapsedTime = offloadElapsedTime;
            if (settings.TryGetValue<bool>(nameof(SendDirtyValuesOnly), out var sendDirtyValuesOnly))
                SendDirtyValuesOnly = sendDirtyValuesOnly;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Identifier}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ip/host:port"), endpointString =>
        {
            if (NetUtils.TryParseEndpoint(endpointString, out var endpoint))
                Endpoint = endpoint;
        });
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::Endpoint::Set");
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(Endpoint);
            client.GetStream();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
