using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.TCode;
using MultiFunPlayer.Shortcut;
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
internal sealed class UdpOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider, IInputProcessorFactory inputProcessorFactory)
    : ThreadAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public DeviceAxisUpdateType UpdateType { get; set; } = DeviceAxisUpdateType.FixedUpdate;
    public bool CanChangeUpdateType => !IsConnectBusy && !IsConnected;

    public EndPoint Endpoint { get; set; } = new DnsEndPoint("tcode.local", 8000);

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new TCodeThreadFixedUpdateContext(),
        DeviceAxisUpdateType.PolledUpdate => new ThreadPolledUpdateContext(),
        _ => null,
    };

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Identifier, Endpoint?.ToUriString(), connectionType);

        if (Endpoint == null)
            throw new OutputTargetException("Endpoint cannot be null");

        return ValueTask.FromResult(true);
    }

    protected override void Run(ConnectionType connectionType, CancellationToken token)
    {
        using var client = new UdpClient();

        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            client.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, [0, 0, 0, 0], null);

            client.Connect(Endpoint);
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
            EventAggregator.Publish(new SyncRequestMessage());

            using var tcodeInputProcessor = inputProcessorFactory.GetInputProcessor<TCodeInputProcessor>();

            var buffer = new byte[256];
            if (UpdateType == DeviceAxisUpdateType.FixedUpdate)
            {
                var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
                FixedUpdate<TCodeThreadFixedUpdateContext>(() => !token.IsCancellationRequested, (context, elapsed) =>
                {
                    Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                    GetValues(currentValues);

                    if (client.Available > 0)
                    {
                        var endpoint = new IPEndPoint(IPAddress.Any, 0);
                        OnDataReceived(client.Receive(ref endpoint), endpoint);
                    }

                    var values = context.SendDirtyValuesOnly ? currentValues.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : currentValues;
                    values = values.Where(x => AxisSettings[x.Key].Enabled);

                    var commands = context.OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                    if (!string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), $"udp://{Endpoint.ToUriString()}");

                        var encoded = Encoding.UTF8.GetBytes(commands, buffer);
                        client.Send(buffer, encoded);
                        lastSentValues.Merge(values);
                    }
                });
            }
            else if (UpdateType == DeviceAxisUpdateType.PolledUpdate)
            {
                client.BeginReceive(ReceiveCallback, null);
                PolledUpdate(DeviceAxis.All, () => !token.IsCancellationRequested, (_, axis, snapshot, elapsed) =>
                {
                    Logger.Trace("Begin PolledUpdate [Axis: {0}, Index From: {1}, Index To: {2}, Duration: {3}, Elapsed: {4}]",
                        axis, snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);

                    var settings = AxisSettings[axis];
                    if (!settings.Enabled)
                        return;
                    if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                        return;

                    var value = MathUtils.Lerp(settings.Minimum / 100, settings.Maximum / 100, snapshot.KeyframeTo.Value);
                    var duration = snapshot.Duration;

                    var command = DeviceAxis.ToString(axis, value, duration * 1000);
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", command, $"udp://{Endpoint.ToUriString()}");

                        var encoded = Encoding.UTF8.GetBytes($"{command}\n", buffer);
                        client.Send(buffer, encoded);
                    }
                }, token);

                void ReceiveCallback(IAsyncResult result)
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    OnDataReceived(client.EndReceive(result, ref endpoint), endpoint);
                    client.BeginReceive(ReceiveCallback, null);
                }
            }

            void OnDataReceived(byte[] bytes, IPEndPoint endpoint)
            {
                var message = Encoding.UTF8.GetString(bytes);
                Logger.Debug("Received \"{0}\" from \"{1}\"", message, $"udp://{endpoint.ToUriString()}");
                tcodeInputProcessor.Parse(message);
            }
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
            settings[nameof(UpdateType)] = JToken.FromObject(UpdateType);
            settings[nameof(Endpoint)] = Endpoint?.ToUriString();
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DeviceAxisUpdateType>(nameof(UpdateType), out var updateType))
                UpdateType = updateType;
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region Endpoint
        s.RegisterAction<string>($"{Identifier}::Endpoint::Set", s => s.WithLabel("Endpoint").WithDescription("ipOrHost:port"), endpointString =>
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
}
