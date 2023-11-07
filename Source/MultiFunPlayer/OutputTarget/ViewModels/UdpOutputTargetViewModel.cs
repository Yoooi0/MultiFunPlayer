using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.TCode;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("UDP")]
internal class UdpOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IInputProcessorManager _inputManager;

    public override ConnectionStatus Status { get; protected set; }

    public bool OffloadElapsedTime { get; set; } = true;
    public bool SendDirtyValuesOnly { get; set; } = false;
    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);

    public UdpOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider, IInputProcessorManager inputManager)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        _inputManager = inputManager;
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
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
            Logger.Error(e, "Error when connecting to server");
            _ = DialogHelper.ShowErrorAsync(e, "Error when connecting to server", "RootDialog");
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            using var _ = _inputManager.Register<TCodeInputProcessor>(out var tcodeInputProcessor);

            var buffer = new byte[256];
            var receiveBuffer = new SplittingStringBuffer('\n');
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (client.Available > 0)
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var message = Encoding.UTF8.GetString(client.Receive(ref endpoint));
                    Logger.Debug("Received \"{0}\" from \"{1}\"", message, $"udp://{endpoint}");

                    receiveBuffer.Push(message);
                    foreach (var command in receiveBuffer.Consume())
                        tcodeInputProcessor.Parse(command);
                }

                var values = SendDirtyValuesOnly ? Values.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : Values;
                values = values.Where(x => AxisSettings[x.Key].Enabled);

                var commands = OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                if (!string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"udp://{Endpoint}");

                    var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                    client.Send(buffer, encoded);
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

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(true);
}
