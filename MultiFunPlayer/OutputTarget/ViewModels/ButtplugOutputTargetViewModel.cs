using Buttplug;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Windows;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Buttplug.io")]
public class ButtplugOutputTargetViewModel : AsyncAbstractOutputTarget
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly List<ServerMessage.Types.MessageAttributeType> _supportedMessages = new()
    {
        ServerMessage.Types.MessageAttributeType.LinearCmd,
        ServerMessage.Types.MessageAttributeType.RotateCmd,
        ServerMessage.Types.MessageAttributeType.VibrateCmd
    };
    private SemaphoreSlim _startScanSemaphore;
    private SemaphoreSlim _endScanSemaphore;

    public override int MinimumUpdateInterval => 16;
    public override int MaximumUpdateInterval => 200;

    public override ConnectionStatus Status { get; protected set; }
    public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 12345);
    public ObservableConcurrentCollection<ButtplugClientDevice> AvailableDevices { get; protected set; }

    [DependsOn(nameof(SelectedDevice))]
    public ObservableConcurrentCollection<ServerMessage.Types.MessageAttributeType> AvailableMessageTypes
        => SelectedDevice != null ? new(SelectedDevice.AllowedMessages.Keys.Join(_supportedMessages, x => x, x => x, (x, _) => x)) : null;

    [DependsOn(nameof(SelectedDevice), nameof(AvailableMessageTypes))]
    public ObservableConcurrentCollection<uint> AvailableFeatureIndices
    {
        get
        {
            if (SelectedDevice == null || SelectedMessageType == null)
                return null;

            var indices = Enumerable.Range(0, (int)SelectedDevice.AllowedMessages[SelectedMessageType.Value].FeatureCount).Select(x => (uint)x);
            var usedIndices = DeviceSettings.Where(s => string.Equals(s.DeviceName, SelectedDevice.Name, StringComparison.OrdinalIgnoreCase) && s.MessageType == SelectedMessageType)
                                            .Select(s => s.FeatureIndex);
            var allowedIndices = indices.Except(usedIndices);
            if (!allowedIndices.Any())
                return null;

            return new(allowedIndices);
        }
    }

    public ButtplugClientDevice SelectedDevice { get; set; }
    public DeviceAxis SelectedDeviceAxis { get; set; }
    public ServerMessage.Types.MessageAttributeType? SelectedMessageType { get; set; }
    public uint? SelectedFeatureIndex { get; set; }
    public bool CanAddSelected => SelectedDevice != null && SelectedDeviceAxis != null && SelectedMessageType != null && SelectedFeatureIndex != null;

    public ObservableConcurrentCollection<ButtplugClientDeviceSettings> DeviceSettings { get; protected set; }

    public ButtplugOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(shortcutManager, eventAggregator, valueProvider)
    {
        AvailableDevices = new ObservableConcurrentCollection<ButtplugClientDevice>();
        DeviceSettings = new ObservableConcurrentCollection<ButtplugClientDeviceSettings>();
        UpdateInterval = 50;

        AvailableDevices.CollectionChanged += (s, e) => DeviceSettings.Refresh();

        var rule = LogManager.Configuration.LoggingRules.FirstOrDefault(r => r.Targets.Any(t => string.Equals(t.Name, "file", StringComparison.OrdinalIgnoreCase)));
        var logLevel = (rule?.Levels.Min().Ordinal ?? 2) switch
        {
            0 => ButtplugLogLevel.Trace,
            1 => ButtplugLogLevel.Debug,
            2 => ButtplugLogLevel.Info,
            3 => ButtplugLogLevel.Warn,
            4 or 5 => ButtplugLogLevel.Error,
            6 => ButtplugLogLevel.Off,
            _ => ButtplugLogLevel.Info
        };

        ButtplugFFILog.LogMessage += (_, m) =>
        {
            var prefix = m.Remove(25).Trim();
            var level = prefix[^5..].Trim() switch
            {
                "TRACE" => LogLevel.Trace,
                "DEBUG" => LogLevel.Debug,
                "INFO" => LogLevel.Info,
                "WARN" => LogLevel.Warn,
                "ERROR" => LogLevel.Error,
                "OFF" => LogLevel.Off,
                _ => LogLevel.Info,
            };

            var message = m.Remove(0, 25).Trim();
            Logger.Log(level, message);
        };

        ButtplugFFILog.SetLogOptions(logLevel, false);
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
        void OnDeviceRemoved(ButtplugClientDevice device)
        {
            Logger.Info($"Device removed: \"{device.Name}\"");

            foreach (var settings in DeviceSettings.Where(s => GetDeviceFromSettings(s) == device))
                settings.IsConnected = false;

            AvailableDevices.Remove(device);
            if (device == SelectedDevice)
                SelectedDevice = null;
        }

        void OnDeviceAdded(ButtplugClientDevice device)
        {
            Logger.Info($"Device added: \"{device.Name}\"");

            AvailableDevices.Add(device);

            foreach (var settings in DeviceSettings.Where(s => GetDeviceFromSettings(s) == device))
                settings.IsConnected = true;
        }

        using var client = new ButtplugClient(nameof(MultiFunPlayer));
        client.DeviceAdded += (_, e) => OnDeviceAdded(e.Device);
        client.DeviceRemoved += (_, e) => OnDeviceRemoved(e.Device);
        client.ErrorReceived += (_, e) => Logger.Debug(e.Exception);
        client.ScanningFinished += (_, _) =>
        {
            if (IsScanBusy && _endScanSemaphore?.CurrentCount == 0)
                _endScanSemaphore.Release();
        };

        try
        {
            Logger.Info("Connecting to {0}", $"ws://{Endpoint}");
            await client.ConnectAsync(new ButtplugWebsocketConnectorOptions(new Uri($"ws://{Endpoint}"))).WithCancellation(token);
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Error when connecting to server");
            if (client.Connected)
                await client.DisconnectAsync();

            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Error when connecting to server:\n\n{e}"), "RootDialog"));
            return;
        }

        try
        {
            _ = ScanAsync(client, token);

            var lastSentValuesPerDevice = new Dictionary<ButtplugClientDevice, Dictionary<DeviceAxis, float>>();
            bool CheckDirtyAndUpdate(ButtplugClientDeviceSettings settings)
            {
                var device = GetDeviceFromSettings(settings);
                if (device == null)
                    return false;

                if (!lastSentValuesPerDevice.ContainsKey(device))
                    lastSentValuesPerDevice.Add(device, DeviceAxis.All.ToDictionary(a => a, _ => float.NaN));

                var axis = settings.SourceAxis;
                var lastSentValues = lastSentValuesPerDevice[device];
                var currentValue = Values[axis];
                var lastValue = lastSentValues[axis];

                if (!float.IsFinite(currentValue)) 
                    return false;

                var shouldUpdate = !float.IsFinite(lastValue)
                                || (currentValue == 0 && lastValue != 0)
                                || MathF.Abs(lastValue - currentValue) >= 0.005f;

                if(shouldUpdate)
                    lastSentValues[axis] = currentValue;

                return shouldUpdate;
            }

            EventAggregator.Publish(new SyncRequestMessage());

            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && client.Connected)
            {
                UpdateValues();

                foreach (var orphanedSettings in lastSentValuesPerDevice.Keys.Except(AvailableDevices))
                    lastSentValuesPerDevice.Remove(orphanedSettings);

                var dirtySettings = DeviceSettings.Where(CheckDirtyAndUpdate);
                var tasks = GetDeviceTasks(UpdateInterval, dirtySettings);

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception)
                {
                    foreach (var exception in tasks.Where(t => t.Exception != null).Select(t => t.Exception))
                        Logger.Debug(exception, "Buttplug device exception");
                }

                await Sleep(stopwatch, token);
                stopwatch.Restart();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog"));
        }

        if (client.Connected)
            await client.DisconnectAsync();

        IsScanBusy = false;
        AvailableDevices.Clear();
    }

    private ButtplugClientDevice GetDeviceFromSettings(ButtplugClientDeviceSettings settings)
        => GetDeviceByNameAndIndex(settings.DeviceName, settings.DeviceIndex);
    private ButtplugClientDevice GetDeviceByNameAndIndex(string deviceName, uint deviceIndex)
        => AvailableDevices.FirstOrDefault(d => string.Equals(deviceName, d.Name, StringComparison.OrdinalIgnoreCase) && deviceIndex == d.Index);

    protected override float CoerceProviderValue(DeviceAxis axis, float value)
    {
        value = base.CoerceProviderValue(axis, value);
        return value < 0.005f ? 0 : value;
    }

    private IEnumerable<Task> GetDeviceTasks(float interval, IEnumerable<ButtplugClientDeviceSettings> settings)
    {
        return settings.GroupBy(m => m.DeviceName).SelectMany(deviceGroup =>
        {
            var deviceName = deviceGroup.Key;
            return deviceGroup.GroupBy(m => m.DeviceIndex).SelectMany(indexGroup =>
            {
                var deviceIndex = indexGroup.Key;
                var device = GetDeviceByNameAndIndex(deviceName, deviceIndex);
                if(device == null)
                    return Enumerable.Empty<Task>();

                return indexGroup.GroupBy(m => m.MessageType).Select(typeGroup =>
                {
                    var type = typeGroup.Key;
                    if (type == ServerMessage.Types.MessageAttributeType.VibrateCmd)
                    {
                        var cmds = typeGroup.ToDictionary(m => m.FeatureIndex,
                                                            m => (double)Values[m.SourceAxis]);

                        Logger.Trace("Sending vibrate commands \"{data}\" to \"{name}\"", cmds, device.Name);
                        return device.SendVibrateCmd(cmds);
                    }

                    if (type == ServerMessage.Types.MessageAttributeType.LinearCmd)
                    {
                        var cmds = typeGroup.ToDictionary(m => m.FeatureIndex,
                                                            m => ((uint)interval, (double)Values[m.SourceAxis]));

                        Logger.Trace("Sending linear commands \"{data}\" to \"{name}\"", cmds, device.Name);
                        return device.SendLinearCmd(cmds);
                    }

                    if (type == ServerMessage.Types.MessageAttributeType.RotateCmd)
                    {
                        var cmds = typeGroup.ToDictionary(m => m.FeatureIndex,
                                                            m => (Math.Clamp(Math.Abs(Values[m.SourceAxis] - 0.5) / 0.5, 0, 1), Values[m.SourceAxis] > 0.5));

                        Logger.Trace("Sending rotate commands \"{data}\" to \"{name}\"", cmds, device.Name);
                        return device.SendRotateCmd(cmds);
                    }

                    return Task.CompletedTask;
                });
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

        try { await client.StopScanningAsync().WithCancellation(token); } catch { }

        CleanupSemaphores();
        _startScanSemaphore = new SemaphoreSlim(1, 1);
        _endScanSemaphore = new SemaphoreSlim(0, 1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _startScanSemaphore.WaitAsync(token);
                    await client.StartScanningAsync().WithCancellation(token);

                    IsScanBusy = true;
                    await _endScanSemaphore.WaitAsync(token);
                    IsScanBusy = false;

                    if (client.IsScanning)
                        await client.StopScanningAsync().WithCancellation(token);
                }
                catch (ButtplugException) { }
            }
        }
        catch (OperationCanceledException) { }

        CleanupSemaphores();
    }

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        base.HandleSettings(settings, type);

        if (type == AppSettingsMessageType.Saving)
        {
            if (Endpoint != null)
                settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());

            if (DeviceSettings != null)
                settings[nameof(DeviceSettings)] = JArray.FromObject(DeviceSettings);
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<IPEndPoint>(nameof(Endpoint), out var endpoint))
                Endpoint = endpoint;

            if (settings.TryGetValue<List<ButtplugClientDeviceSettings>>(nameof(DeviceSettings), out var deviceSettings))
            {
                DeviceSettings.Clear();
                DeviceSettings.AddRange(deviceSettings);
            }
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

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://{Endpoint}"), token);
            var result = client.State == WebSocketState.Open;
            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
            return await ValueTask.FromResult(result);
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }

    public void OnSettingsAdd()
    {
        DeviceSettings.Add(new()
        {
            DeviceName = SelectedDevice.Name,
            DeviceIndex = SelectedDevice.Index,
            SourceAxis = SelectedDeviceAxis,
            FeatureIndex = SelectedFeatureIndex.Value,
            MessageType = SelectedMessageType.Value
        });

        SelectedDevice = null;
        SelectedDeviceAxis = null;
        SelectedFeatureIndex = null;
        SelectedMessageType = null;
    }

    public void OnSettingsDelete(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ButtplugClientDeviceSettings settings)
            return;

        DeviceSettings.Remove(settings);
        NotifyOfPropertyChange(nameof(AvailableFeatureIndices));
    }
}

[JsonObject(MemberSerialization.OptIn)]
public class ButtplugClientDeviceSettings : PropertyChangedBase
{
    [JsonProperty] public string DeviceName { get; set; }
    [JsonProperty] public uint DeviceIndex { get; set; }
    [JsonProperty] public DeviceAxis SourceAxis { get; set; }
    [JsonProperty] public ServerMessage.Types.MessageAttributeType MessageType { get; set; }
    [JsonProperty] public uint FeatureIndex { get; set; }
    public bool IsConnected { get; set; }
}
