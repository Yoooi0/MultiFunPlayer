using Buttplug;
using Buttplug.NewtonsoftJson;
using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Net;
using System.Net.WebSockets;
using System.Windows;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Buttplug.io")]
internal sealed class ButtplugOutputTarget : AsyncAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private SemaphoreSlim _startScanSemaphore;
    private SemaphoreSlim _endScanSemaphore;

    public override int MinimumUpdateInterval => 16;
    public override int MaximumUpdateInterval => 200;

    public override ConnectionStatus Status { get; protected set; }
    public EndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 12345);
    public ObservableConcurrentCollection<ButtplugDevice> AvailableDevices { get; }

    [DependsOn(nameof(SelectedDevice))]
    public IReadOnlyCollection<ActuatorType> AvailableActuatorTypes
        => SelectedDevice != null ? [.. SelectedDevice.Actuators.Select(a => a.ActuatorType).Distinct()] : null;

    [DependsOn(nameof(SelectedDevice), nameof(AvailableActuatorTypes))]
    public IReadOnlyCollection<uint> AvailableActuatorIndices
    {
        get
        {
            if (SelectedDevice == null || SelectedActuatorType == null)
                return null;

            var actuators = SelectedDevice.GetActuators(SelectedActuatorType.Value);
            var indices = actuators.Select(a => a.Index);
            var usedIndices = GetSettingsForDevice(SelectedDevice).Where(s => s.ActuatorType == SelectedActuatorType.Value).Select(s => s.ActuatorIndex);
            var allowedIndices = indices.Except(usedIndices);
            if (!allowedIndices.Any())
                return null;

            return [.. allowedIndices];
        }
    }

    public ButtplugDevice SelectedDevice { get; set; }
    public DeviceAxis SelectedDeviceAxis { get; set; }
    public ActuatorType? SelectedActuatorType { get; set; }
    public uint? SelectedActuatorIndex { get; set; }
    public bool CanAddSelected => SelectedDevice != null && SelectedDeviceAxis != null && SelectedActuatorType != null && SelectedActuatorIndex != null;

    public ObservableConcurrentCollection<ButtplugDeviceSettings> DeviceSettings { get; }

    public ButtplugOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        AvailableDevices = [];
        DeviceSettings = [];
        UpdateInterval = 50;

        AvailableDevices.CollectionChanged += (s, e) => DeviceSettings.Refresh();
    }

    public bool IsScanBusy { get; set; }
    public bool CanScan => IsConnected;
    public void ToggleScan()
    {
        if (IsScanBusy && _endScanSemaphore?.CurrentCount == 0)
            _endScanSemaphore.Release();
        else if (_startScanSemaphore?.CurrentCount == 0)
            _startScanSemaphore.Release();
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override async Task RunAsync(CancellationToken token)
    {
        void OnDeviceRemoved(ButtplugDevice device)
        {
            Logger.Info($"Device removed: \"{device.Name}\"");

            AvailableDevices.Remove(device);
            if (device == SelectedDevice)
                SelectedDevice = null;
        }

        void OnDeviceAdded(ButtplugDevice device)
        {
            Logger.Info($"Device added: \"{device.Name}\"");

            AvailableDevices.Add(device);

            var syncAxes = GetSettingsForDevice(device)
                                .GroupBy(s => s.SourceAxis)
                                .Select(g => g.Key);
            EventAggregator.Publish(new SyncRequestMessage(syncAxes));
        }

        var converter = new ButtplugNewtonsoftJsonConverter();
        await using var client = new ButtplugClient(nameof(MultiFunPlayer), converter);

        client.DeviceAdded += (_, device) => OnDeviceAdded(device);
        client.DeviceRemoved += (_, device) => OnDeviceRemoved(device);
        client.UnhandledException += (_, exception) => Logger.Debug(exception);
        client.ScanningFinished += (_, _) =>
        {
            if (IsScanBusy && _endScanSemaphore?.CurrentCount == 0)
                _endScanSemaphore.Release();
        };

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, $"ws://{Endpoint.ToUriString()}");
            await client.ConnectAsync(new Uri($"ws://{Endpoint.ToUriString()}"), token);
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
            _ = ScanAsync(client, token);

            var lastSentValuesPerDevice = new Dictionary<ButtplugDevice, Dictionary<DeviceAxis, double>>();
            bool CheckDirtyAndUpdate(ButtplugDeviceSettings settings)
            {
                var device = GetDeviceFromSettings(settings);
                if (device == null)
                    return false;

                if (!lastSentValuesPerDevice.ContainsKey(device))
                    lastSentValuesPerDevice.Add(device, DeviceAxis.All.ToDictionary(a => a, _ => double.NaN));

                var axis = settings.SourceAxis;
                var lastSentValues = lastSentValuesPerDevice[device];
                var currentValue = Values[axis];
                var lastValue = lastSentValues[axis];

                if (!double.IsFinite(currentValue))
                    return false;
                if (!AxisSettings[axis].Enabled)
                    return false;

                var shouldUpdate = !double.IsFinite(lastValue)
                                || (currentValue == 0 && lastValue != 0)
                                || Math.Abs(lastValue - currentValue) >= 0.005;

                if (shouldUpdate)
                    lastSentValues[axis] = currentValue;

                return shouldUpdate;
            }

            EventAggregator.Publish(new SyncRequestMessage());

            await FixedUpdateAsync(() => !token.IsCancellationRequested && client.IsConnected, async elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                foreach (var orphanedDevice in lastSentValuesPerDevice.Keys.Except(AvailableDevices))
                    lastSentValuesPerDevice.Remove(orphanedDevice);

                foreach (var orphanedDevice in AvailableDevices.Where(d => lastSentValuesPerDevice.ContainsKey(d) && !GetSettingsForDevice(d).Any()))
                {
                    await orphanedDevice.StopAsync(token);
                    lastSentValuesPerDevice.Remove(orphanedDevice);
                }

                var dirtySettings = DeviceSettings.Where(CheckDirtyAndUpdate);
                var tasks = GetDeviceTasks(elapsed * 1000, dirtySettings, token);

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception)
                {
                    foreach (var exception in tasks.Where(t => t.Exception != null).Select(t => t.Exception))
                        Logger.Debug(exception, "Buttplug device exception");
                }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        AvailableDevices.Clear();
    }

    private ButtplugDevice GetDeviceFromSettings(ButtplugDeviceSettings settings)
        => GetDeviceByNameAndIndex(settings.DeviceName, settings.DeviceIndex);
    private ButtplugDevice GetDeviceByNameAndIndex(string deviceName, uint deviceIndex)
        => AvailableDevices.FirstOrDefault(d => string.Equals(deviceName, d.Name, StringComparison.OrdinalIgnoreCase) && deviceIndex == d.Index);
    private IEnumerable<ButtplugDeviceSettings> GetSettingsForDevice(ButtplugDevice device)
        => DeviceSettings.Where(s => string.Equals(s.DeviceName, device.Name, StringComparison.OrdinalIgnoreCase) && s.DeviceIndex == device.Index);

    protected override double CoerceProviderValue(DeviceAxis axis, double value)
    {
        value = base.CoerceProviderValue(axis, value);
        return value < 0.005 ? 0 : value;
    }

    private IEnumerable<Task> GetDeviceTasks(double interval, IEnumerable<ButtplugDeviceSettings> settings, CancellationToken token)
    {
        return settings.GroupBy(m => m.DeviceName).SelectMany(deviceGroup =>
        {
            var deviceName = deviceGroup.Key;
            return deviceGroup.GroupBy(m => m.DeviceIndex).SelectMany(indexGroup =>
            {
                var deviceIndex = indexGroup.Key;
                var device = GetDeviceByNameAndIndex(deviceName, deviceIndex);
                if (device == null)
                    return Enumerable.Empty<Task>();

                return indexGroup.GroupBy(m => m.ActuatorType).SelectMany(typeGroup => typeGroup.Select(s =>
                {
                    if (!device.TryGetActuator(s.ActuatorIndex, s.ActuatorType, out var actuator))
                        return Task.CompletedTask;

                    var value = Values[s.SourceAxis];
                    if (actuator is ButtplugDeviceLinearActuator linearActuator)
                    {
                        var duration = (uint)Math.Floor(interval + 0.75);
                        Logger.Trace("Sending \"{value} (Duration={duration})\" to \"{actuator}\"", value, duration, actuator);
                        return linearActuator.LinearAsync(duration, value, token);
                    }

                    if (actuator is ButtplugDeviceRotateActuator rotateActuator)
                    {
                        var speed = Math.Clamp(Math.Abs(value - 0.5) / 0.5, 0, 1);
                        var clockwise = value > 0.5;
                        Logger.Trace("Sending \"{speed} (Clockwise={clockwise})\" to \"{actuator}\"", speed, clockwise, actuator);
                        return rotateActuator.RotateAsync(speed, clockwise, token);
                    }

                    if (actuator is ButtplugDeviceScalarActuator scalarActuator)
                    {
                        Logger.Trace("Sending \"{value}\" to \"{actuator}\"", value, actuator);
                        return scalarActuator.ScalarAsync(value, token);
                    }

                    return Task.CompletedTask;
                }));
            });
        });
    }

    private async Task ScanAsync(ButtplugClient client, CancellationToken token)
    {
        void CleanupSemaphores()
        {
            _startScanSemaphore?.Dispose();
            _endScanSemaphore?.Dispose();

            _startScanSemaphore = null;
            _endScanSemaphore = null;
        }

        await client.StopScanningAsync(token);

        CleanupSemaphores();
        _startScanSemaphore = new SemaphoreSlim(1, 1);
        _endScanSemaphore = new SemaphoreSlim(0, 1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await _startScanSemaphore.WaitAsync(token);
                await client.StartScanningAsync(token);

                IsScanBusy = true;
                await _endScanSemaphore.WaitAsync(token);
                IsScanBusy = false;

                if (client.IsScanning)
                    await client.StopScanningAsync(token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsScanBusy = false;
        }

        CleanupSemaphores();
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(Endpoint)] = Endpoint?.ToUriString();
            settings[nameof(DeviceSettings)] = JArray.FromObject(DeviceSettings);
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<EndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;

            if (settings.TryGetValue<List<ButtplugDeviceSettings>>(nameof(DeviceSettings), out var deviceSettings))
                DeviceSettings.SetFrom(deviceSettings.Where(d => d.SourceAxis != null));
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
            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://{Endpoint.ToUriString()}"), token);
            var result = client.State == WebSocketState.Open;
            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
            return result;
        }
        catch
        {
            return false;
        }
    }

    public void OnSettingsAdd()
    {
        DeviceSettings.Add(new()
        {
            DeviceName = SelectedDevice.Name,
            DeviceIndex = SelectedDevice.Index,
            SourceAxis = SelectedDeviceAxis,
            ActuatorIndex = SelectedActuatorIndex.Value,
            ActuatorType = SelectedActuatorType.Value
        });

        SelectedDevice = null;
        SelectedDeviceAxis = null;
        SelectedActuatorIndex = null;
        SelectedActuatorType = null;
    }

    public void OnSettingsDelete(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ButtplugDeviceSettings settings)
            return;

        DeviceSettings.Remove(settings);
        NotifyOfPropertyChange(nameof(AvailableActuatorIndices));
    }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class ButtplugDeviceSettings : PropertyChangedBase
{
    [JsonProperty] public string DeviceName { get; set; }
    [JsonProperty] public uint DeviceIndex { get; set; }
    [JsonProperty] public DeviceAxis SourceAxis { get; set; }
    [JsonProperty] public ActuatorType ActuatorType { get; set; }
    [JsonProperty] public uint ActuatorIndex { get; set; }
}
