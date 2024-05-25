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
using System.Windows;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Buttplug.io")]
internal sealed class ButtplugOutputTarget : AsyncAbstractOutputTarget
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private SemaphoreSlim _scanSemaphore;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

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

        AvailableDevices.CollectionChanged += (s, e) => DeviceSettings.Refresh();

        _scanSemaphore = new SemaphoreSlim(1, 1);
    }

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new AsyncFixedUpdateContext() { UpdateInterval = 50, MinimumUpdateInterval = 16, MaximumUpdateInterval = 200 },
        DeviceAxisUpdateType.PolledUpdate => new AsyncPolledUpdateContext(),
        _ => throw new NotImplementedException(),
    };

    public bool IsScanBusy { get; set; }
    public bool CanScan => IsConnected;
    public void ToggleScan() => _scanSemaphore.Release();

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Identifier, Endpoint?.ToUriString(), connectionType);

        if (Endpoint == null)
            throw new OutputTargetException("Endpoint cannot be null");

        return ValueTask.FromResult(true);
    }

    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
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

        try
        {
            await client.ConnectAsync(new Uri($"ws://{Endpoint.ToUriString()}"), token);
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
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var task = await Task.WhenAny(FixedUpdateAsync(client, cancellationSource.Token), PolledUpdateAsync(client, cancellationSource.Token), ScanAsync(client, cancellationSource.Token));
            cancellationSource.Cancel();

            task.ThrowIfFaulted();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        AvailableDevices.Clear();
    }

    private async Task FixedUpdateAsync(ButtplugClient client, CancellationToken token)
    {
        var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
        var lastSentValuesPerDevice = new Dictionary<ButtplugDevice, Dictionary<DeviceAxis, double>>();
        bool CheckDirtyAndUpdate(ButtplugDeviceSettings settings)
        {
            if (settings.UpdateType != DeviceAxisUpdateType.FixedUpdate)
                return false;

            var device = GetDeviceFromSettings(settings);
            if (device == null)
                return false;

            if (!lastSentValuesPerDevice.ContainsKey(device))
                lastSentValuesPerDevice.Add(device, DeviceAxis.All.ToDictionary(a => a, _ => double.NaN));

            var axis = settings.SourceAxis;
            var lastSentValues = lastSentValuesPerDevice[device];
            var currentValue = currentValues[axis];
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

        await FixedUpdateAsync(() => !token.IsCancellationRequested && client.IsConnected, async (_, elapsed) =>
        {
            Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
            GetValues(currentValues, x => x < 0.005 ? 0 : x);

            foreach (var orphanedDevice in lastSentValuesPerDevice.Keys.Except(AvailableDevices))
                lastSentValuesPerDevice.Remove(orphanedDevice);

            foreach (var orphanedDevice in AvailableDevices.Where(d => lastSentValuesPerDevice.ContainsKey(d) && !GetSettingsForDevice(d).Any()))
            {
                await orphanedDevice.StopAsync(token);
                lastSentValuesPerDevice.Remove(orphanedDevice);
            }

            var dirtySettings = DeviceSettings.Where(CheckDirtyAndUpdate);
            var tasks = GetDeviceTasks(currentValues, elapsed * 1000, dirtySettings, token);

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

    private async Task PolledUpdateAsync(ButtplugClient client, CancellationToken token)
    {
        await PolledUpdateAsync(DeviceAxis.All, () => !token.IsCancellationRequested && client.IsConnected, async (_, axis, snapshot, elapsed) =>
        {
            Logger.Trace("Begin PolledUpdate [Axis: {0}, Index From: {1}, Index To: {2}, Duration: {3}, Elapsed: {4}]", axis, snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);

            var settings = DeviceSettings.Where(x => x.SourceAxis == axis && x.UpdateType == DeviceAxisUpdateType.PolledUpdate);
            var tasks = GetDeviceTasks(snapshot, settings, token);

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

    private ButtplugDevice GetDeviceFromSettings(ButtplugDeviceSettings settings)
        => GetDeviceByNameAndIndex(settings.DeviceName, settings.DeviceIndex);
    private ButtplugDevice GetDeviceByNameAndIndex(string deviceName, uint deviceIndex)
        => AvailableDevices.FirstOrDefault(d => string.Equals(deviceName, d.Name, StringComparison.OrdinalIgnoreCase) && deviceIndex == d.Index);
    private IEnumerable<ButtplugDeviceSettings> GetSettingsForDevice(ButtplugDevice device)
        => DeviceSettings.Where(s => string.Equals(s.DeviceName, device.Name, StringComparison.OrdinalIgnoreCase) && s.DeviceIndex == device.Index);

    private IEnumerable<Task> GetDeviceTasks(Dictionary<DeviceAxis, double> values, double interval, IEnumerable<ButtplugDeviceSettings> settings, CancellationToken token)
        => GetDeviceTasks(settings, (s, a) =>
        {
            var value = values[s.SourceAxis];
            if (a is ButtplugDeviceLinearActuator linearActuator)
            {
                var duration = (uint)Math.Floor(interval + 0.75);
                Logger.Trace("Sending \"{value} (Duration={duration})\" to \"{actuator}\"", value, duration, a);
                return linearActuator.LinearAsync(duration, value, token);
            }
            else if (a is ButtplugDeviceRotateActuator rotateActuator)
            {
                var speed = Math.Clamp(Math.Abs(value - 0.5) / 0.5, 0, 1);
                var clockwise = value > 0.5;
                Logger.Trace("Sending \"{speed} (Clockwise={clockwise})\" to \"{actuator}\"", speed, clockwise, a);
                return rotateActuator.RotateAsync(speed, clockwise, token);
            }
            else if (a is ButtplugDeviceScalarActuator scalarActuator)
            {
                Logger.Trace("Sending \"{value}\" to \"{actuator}\"", value, a);
                return scalarActuator.ScalarAsync(value, token);
            }

            return Task.CompletedTask;
        }, token);

    private IEnumerable<Task> GetDeviceTasks(DeviceAxisScriptSnapshot snapshot, IEnumerable<ButtplugDeviceSettings> settings, CancellationToken token)
        => GetDeviceTasks(settings, (s, a) =>
        {
            if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                return Task.CompletedTask;

            var axisSettings = AxisSettings[s.SourceAxis];
            var value = MathUtils.Lerp(axisSettings.Minimum, axisSettings.Maximum, snapshot.KeyframeTo.Value);
            if (a is ButtplugDeviceLinearActuator linearActuator)
            {
                var duration = (uint)Math.Floor(snapshot.Duration * 1000 + 0.75);
                Logger.Trace("Sending \"{value} (Duration={duration})\" to \"{actuator}\"", value, duration, a);
                return linearActuator.LinearAsync(duration, value, token);
            }
            else if (a is ButtplugDeviceRotateActuator rotateActuator)
            {
                var speed = Math.Clamp(Math.Abs(value - 0.5) / 0.5, 0, 1);
                var clockwise = value > 0.5;
                Logger.Trace("Sending \"{speed} (Clockwise={clockwise})\" to \"{actuator}\"", speed, clockwise, a);
                return rotateActuator.RotateAsync(speed, clockwise, token);
            }
            else if (a is ButtplugDeviceScalarActuator scalarActuator)
            {
                Logger.Trace("Sending \"{value}\" to \"{actuator}\"", value, a);
                return scalarActuator.ScalarAsync(value, token);
            }

            return Task.CompletedTask;
        }, token);

    private IEnumerable<Task> GetDeviceTasks(IEnumerable<ButtplugDeviceSettings> settings, Func<ButtplugDeviceSettings, ButtplugDeviceActuator, Task> taskFactory, CancellationToken token)
        => settings.GroupBy(m => m.DeviceName).SelectMany(deviceGroup =>
        {
            var deviceName = deviceGroup.Key;
            return deviceGroup.GroupBy(m => m.DeviceIndex).SelectMany(indexGroup =>
            {
                var deviceIndex = indexGroup.Key;
                var device = GetDeviceByNameAndIndex(deviceName, deviceIndex);
                if (device == null)
                    return [];

                return indexGroup.GroupBy(m => m.ActuatorType).SelectMany(typeGroup => typeGroup.Select(s =>
                {
                    if (!device.TryGetActuator(s.ActuatorIndex, s.ActuatorType, out var actuator))
                        return Task.CompletedTask;

                    return taskFactory(s, actuator);
                }));
            });
        });

    private async Task ScanAsync(ButtplugClient client, CancellationToken token)
    {
        var scanTask = default(Task);
        var cancellationSource = default(CancellationTokenSource);
        while (!token.IsCancellationRequested)
        {
            await _scanSemaphore.WaitAsync(token);
            if (scanTask?.IsCompleted == false)
            {
                cancellationSource?.Cancel();
                cancellationSource?.Dispose();
                cancellationSource = null;
            }
            else
            {
                cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                cancellationSource.CancelAfter(TimeSpan.FromSeconds(5));

                scanTask = DoScanAsync();
            }
        }

        async Task DoScanAsync()
        {
            try
            {
                IsScanBusy = true;

                await client.StartScanningAsync(token);

                try { await Task.Delay(TimeSpan.FromSeconds(30), cancellationSource.Token); }
                catch { }

                await client.StopScanningAsync(token);
            }
            finally
            {
                IsScanBusy = false;
            }
        }
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

    public void OnSettingsAdd()
    {
        DeviceSettings.Add(new()
        {
            DeviceName = SelectedDevice.Name,
            DeviceIndex = SelectedDevice.Index,
            SourceAxis = SelectedDeviceAxis,
            ActuatorIndex = SelectedActuatorIndex.Value,
            ActuatorType = SelectedActuatorType.Value,
            UpdateType = DeviceAxisUpdateType.FixedUpdate
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _scanSemaphore?.Dispose();
        _scanSemaphore = null;
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
    [JsonProperty] public DeviceAxisUpdateType UpdateType { get; set; }
}
