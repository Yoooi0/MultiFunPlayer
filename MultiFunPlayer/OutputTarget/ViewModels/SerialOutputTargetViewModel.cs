using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Serial")]
public class SerialOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private SemaphoreSlim _refreshSemaphore;
    private CancellationTokenSource _refreshCancellationSource;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public bool OffloadElapsedTime { get; set; } = true;
    public ObservableConcurrentCollection<SerialPortInfo> SerialPorts { get; set; }
    public SerialPortInfo SelectedSerialPort { get; set; }
    public string SelectedSerialPortDeviceId { get; set; }

    public SerialOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        SerialPorts = new ObservableConcurrentCollection<SerialPortInfo>();

        _refreshSemaphore = new SemaphoreSlim(1, 1);
        _refreshCancellationSource = new CancellationTokenSource();

        _ = RefreshPorts();
    }

    public bool CanChangePort => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public bool IsRefreshBusy { get; set; }
    public bool CanRefreshPorts => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public async Task RefreshPorts()
    {
        var token = _refreshCancellationSource.Token;

        if (_refreshSemaphore.CurrentCount == 0)
            return;

        await _refreshSemaphore.WaitAsync(token);
        IsRefreshBusy = true;
        await Task.Delay(250, token).ConfigureAwait(true);

        try
        {
            var serialPorts = new List<SerialPortInfo>();
            var scope = new ManagementScope("\\\\.\\ROOT\\cimv2");
            var observer = new ManagementOperationObserver();
            using var searcher = new ManagementObjectSearcher(scope, new SelectQuery("Win32_PnPEntity"));

            observer.ObjectReady += (_, e) =>
            {
                var portInfo = SerialPortInfo.FromManagementObject(e.NewObject as ManagementObject);
                if (portInfo == null)
                    return;

                serialPorts.Add(portInfo);
            };

            var taskCompletion = new TaskCompletionSource();
            observer.Completed += (_, _) => taskCompletion.TrySetResult();

            searcher.Get(observer);
            using (token.Register(() => taskCompletion.TrySetCanceled()))
                await taskCompletion.Task.WaitAsync(token).ConfigureAwait(true);

            var lastSelectedDeviceId = SelectedSerialPortDeviceId;
            SerialPorts.RemoveRange(SerialPorts.Except(serialPorts).ToList());
            SerialPorts.AddRange(serialPorts.Except(SerialPorts).ToList());

            SelectSerialPortByDeviceId(lastSelectedDeviceId);
        }
        catch (Exception e)
        {
            Logger.Warn(e, $"{Identifier} port refresh failed with exception");
        }

        await Task.Delay(250, token).ConfigureAwait(true);
        IsRefreshBusy = false;
        _refreshSemaphore.Release();
    }

    private void SelectSerialPortByDeviceId(string deviceId)
    {
        SelectedSerialPort = SerialPorts.FirstOrDefault(p => string.Equals(p.DeviceID, deviceId, StringComparison.Ordinal));
        if (SelectedSerialPort == null)
            SelectedSerialPortDeviceId = deviceId;
    }

    public void OnSelectedSerialPortChanged() => SelectedSerialPortDeviceId = SelectedSerialPort?.DeviceID;

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && !IsRefreshBusy && SelectedSerialPortDeviceId != null;

    protected override async Task<bool> OnConnectingAsync()
    {
        if (SelectedSerialPortDeviceId == null)
            return false;
        if (SelectedSerialPort == null)
            await RefreshPorts();

        return SelectedSerialPort != null && await base.OnConnectingAsync();
    }

    protected override void Run(CancellationToken token)
    {
        var serialPort = default(SerialPort);

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Identifier, SelectedSerialPortDeviceId);

            serialPort = new SerialPort(SelectedSerialPort.PortName, 115200)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };

            serialPort.Open();
            serialPort.ReadExisting();
            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error when opening serial port");

            try { serialPort?.Close(); }
            catch { }

            _ = Execute.OnUIThreadAsync(() => _ = DialogHelper.ShowErrorAsync(e, "Error when opening serial port", "RootDialog"));
            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested && serialPort.IsOpen, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                if (serialPort.IsOpen && serialPort.BytesToRead > 0)
                    Logger.Debug("Received \"{0}\" from \"{1}\"", serialPort.ReadExisting(), SelectedSerialPortDeviceId);

                var dirtyValues = Values.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key]));
                var commands = OffloadElapsedTime ? DeviceAxis.ToString(dirtyValues) : DeviceAxis.ToString(dirtyValues, elapsed * 1000);
                if (serialPort.IsOpen && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedSerialPortDeviceId);
                    serialPort.Write(commands);
                    lastSentValues.Merge(dirtyValues);
                }
            });
        }
        catch (Exception e) when (e is TimeoutException || e is IOException)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
        catch (Exception e) { Logger.Error(e, $"{Identifier} failed with exception"); }

        try { serialPort?.Close(); }
        catch { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(SelectedSerialPort)] = SelectedSerialPortDeviceId;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedSerialPort), out var selectedSerialPort))
                SelectSerialPortByDeviceId(selectedSerialPort);
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ComPort
        s.RegisterAction($"{Identifier}::SerialPort::Set", b => b.WithSetting<string>(s => s.WithLabel("Device ID")).WithCallback((_, deviceId) => SelectSerialPortByDeviceId(deviceId)));
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::SerialPort::Set");
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            await RefreshPorts();
            if (SelectedSerialPort == null)
                return false;

            using var serialPort = new SerialPort(SelectedSerialPort.PortName, 115200)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };

            serialPort.Open();
            serialPort.ReadExisting();
            serialPort.Close();

            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        _refreshSemaphore?.Dispose();
        _refreshSemaphore = null;

        base.Dispose(disposing);
    }

    public sealed class SerialPortInfo
    {
        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        private SerialPortInfo() { }

        public string Caption { get; private set; }
        public string ClassGuid { get; private set; }
        public string Description { get; private set; }
        public string DeviceID { get; private set; }
        public string Manufacturer { get; private set; }
        public string Name { get; private set; }
        public string PNPClass { get; private set; }
        public string PNPDeviceID { get; private set; }
        public string PortName { get; private set; }

        public static SerialPortInfo FromManagementObject(ManagementObject o)
        {
            T GetPropertyValueOrDefault<T>(string propertyName, T defaultValue = default)
            {
                try { return (T)o.GetPropertyValue(propertyName); }
                catch { return defaultValue; }
            }

            try
            {
                var name = o.GetPropertyValue(nameof(Name)) as string;
                if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\(COM\d+\)"))
                    return null;

                var deviceId = o.GetPropertyValue(nameof(DeviceID)) as string;
                var portName = Registry.GetValue($@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\{deviceId}\Device Parameters", "PortName", "").ToString();

                return new SerialPortInfo()
                {
                    Caption = GetPropertyValueOrDefault<string>(nameof(Caption)),
                    ClassGuid = GetPropertyValueOrDefault<string>(nameof(ClassGuid)),
                    Description = GetPropertyValueOrDefault<string>(nameof(Description)),
                    Manufacturer = GetPropertyValueOrDefault<string>(nameof(Manufacturer)),
                    PNPClass = GetPropertyValueOrDefault<string>(nameof(PNPClass)),
                    PNPDeviceID = GetPropertyValueOrDefault<string>(nameof(PNPDeviceID)),
                    Name = name,
                    DeviceID = deviceId,
                    PortName = portName
                };
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Failed to create SerialPortInfo [{0}]", o?.ToString());
            }

            return null;
        }

        public override bool Equals(object o) => o is SerialPortInfo other && string.Equals(DeviceID, other.DeviceID, StringComparison.Ordinal);
        public override int GetHashCode() => HashCode.Combine(DeviceID);
    }
}
