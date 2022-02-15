using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Serial")]
public class SerialOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private CancellationTokenSource _cancellationSource;

    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public ObservableConcurrentCollection<SerialPortInfo> SerialPorts { get; set; }
    public SerialPortInfo SelectedSerialPort { get; set; }

    [DoNotNotify]
    public string SelectedSerialPortDeviceId { get; set; }

    public SerialOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(shortcutManager, eventAggregator, valueProvider)
    {
        SerialPorts = new ObservableConcurrentCollection<SerialPortInfo>();
        _cancellationSource = new CancellationTokenSource();
    }

    public bool CanChangePort => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public bool IsRefreshBusy { get; set; }
    public bool CanRefreshPorts => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public async Task RefreshPorts()
    {
        var token = _cancellationSource.Token;

        IsRefreshBusy = true;
        await Task.Delay(750, token).ConfigureAwait(true);

        var lastSelectedDeviceId = SelectedSerialPortDeviceId;
        SerialPorts.Clear();
        try
        {
            var scope = new ManagementScope("\\\\.\\ROOT\\cimv2");
            var observer = new ManagementOperationObserver();
            using var searcher = new ManagementObjectSearcher(scope, new SelectQuery("Win32_PnPEntity"));

            observer.ObjectReady += (_, e) =>
            {
                var portInfo = SerialPortInfo.FromManagementObject(e.NewObject as ManagementObject);
                if (portInfo == null)
                    return;

                SerialPorts.Add(portInfo);
            };

            var taskCompletion = new TaskCompletionSource();
            observer.Completed += (_, _) => taskCompletion.TrySetResult();

            searcher.Get(observer);
            using (token.Register(() => taskCompletion.TrySetCanceled()))
                await taskCompletion.Task.WaitAsync(token).ConfigureAwait(true);
        }
        catch { }

        SelectSerialPortByDeviceId(lastSelectedDeviceId);
        await Task.Delay(250, token).ConfigureAwait(true);
        IsRefreshBusy = false;
    }

    private void SelectSerialPortByDeviceId(string deviceId)
    {
        SelectedSerialPort = SerialPorts.FirstOrDefault(p => string.Equals(p.DeviceID, deviceId, StringComparison.Ordinal));
        if(SelectedSerialPort == null)
            SelectedSerialPortDeviceId = deviceId;
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SelectedSerialPort != null;

    public override async Task ConnectAsync()
    {
        if (SelectedSerialPort == null)
            await RefreshPorts();

        await base.ConnectAsync();
    }

    protected override void Run(CancellationToken token)
    {
        var serialPort = default(SerialPort);

        try
        {
            Logger.Info("Connecting to {0} at \"{1}\"", Name, SelectedSerialPortDeviceId);

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
            Logger.Warn(e, "Error when opening serial port");

            try { serialPort?.Close(); }
            catch (IOException) { }

            _ = Execute.OnUIThreadAsync(async () =>
            {
                _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"Error when opening serial port:\n\n{e}"), "RootDialog");
                await RefreshPorts().ConfigureAwait(true);
            });

            return;
        }

        try
        {
            EventAggregator.Publish(new SyncRequestMessage());

            var stopwatch = Stopwatch.StartNew();
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => float.NaN);
            while (!token.IsCancellationRequested && serialPort?.IsOpen == true)
            {
                stopwatch.Restart();
                Sleep(stopwatch);

                UpdateValues();

                if (serialPort?.IsOpen == true && serialPort?.BytesToRead > 0)
                    Logger.Debug("Received \"{0}\" from \"{1}\"", serialPort.ReadExisting(), SelectedSerialPortDeviceId);

                var dirtyValues = Values.Where(x => DeviceAxis.IsDirty(x.Value, lastSentValues[x.Key]));
                var commands = DeviceAxis.ToString(dirtyValues, (float) stopwatch.Elapsed.TotalMilliseconds);
                if (serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedSerialPortDeviceId);
                    serialPort?.Write(commands);
                }

                foreach (var (axis, value) in dirtyValues)
                    lastSentValues[axis] = value;
            }
        }
        catch (Exception e) when (e is TimeoutException || e is IOException)
        {
            Logger.Error(e, $"{Name} failed with exception");
            _ = Execute.OnUIThreadAsync(async () =>
            {
                _ = DialogHelper.ShowOnUIThreadAsync(new ErrorMessageDialogViewModel($"{Name} failed with exception:\n\n{e}"), "RootDialog");
                await RefreshPorts().ConfigureAwait(true);
            });
        }
        catch (Exception e) { Logger.Debug(e, $"{Name} failed with exception"); }

        try { serialPort?.Close(); }
        catch (IOException) { }
    }

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        base.HandleSettings(settings, type);

        if (type == AppSettingsMessageType.Saving)
        {
            settings[nameof(SelectedSerialPort)] = new JValue(SelectedSerialPort?.DeviceID);
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedSerialPort), out var selectedSerialPort))
                SelectSerialPortByDeviceId(selectedSerialPort);
        }
    }

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        #region ComPort
        s.RegisterAction($"{Name}::SerialPort::Set", b => b.WithSetting<string>(s => s.WithLabel("Device ID")).WithCallback((_, deviceId) => SelectSerialPortByDeviceId(deviceId)));
        #endregion
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            await RefreshPorts();
            if (SelectedSerialPort == null)
                return await ValueTask.FromResult(false);

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

            return await ValueTask.FromResult(true);
        }
        catch
        {
            return await ValueTask.FromResult(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();
        _cancellationSource?.Dispose();
        _cancellationSource = null;

        base.Dispose(disposing);
    }

    public class SerialPortInfo
    {
        private SerialPortInfo() { }

        public static SerialPortInfo FromManagementObject(ManagementObject o)
        {
            try
            {
                var name = o.GetPropertyValue(nameof(Name)) as string;
                if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\(COM\d+\)"))
                    return null;

                var deviceId = o.GetPropertyValue(nameof(DeviceID)) as string;
                var portName = Registry.GetValue($@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\{deviceId}\Device Parameters", "PortName", "").ToString();

                return new SerialPortInfo()
                {
                    Caption = o.GetPropertyValue(nameof(Caption)) as string,
                    ClassGuid = o.GetPropertyValue(nameof(ClassGuid)) as string,
                    Description = o.GetPropertyValue(nameof(Description)) as string,
                    Manufacturer = o.GetPropertyValue(nameof(Manufacturer)) as string,
                    PNPClass = o.GetPropertyValue(nameof(PNPClass)) as string,
                    PNPDeviceID = o.GetPropertyValue(nameof(PNPDeviceID)) as string,
                    Name = name,
                    DeviceID = deviceId,
                    PortName = portName
                };
            }
            catch { }

            return null;
        }

        public string Caption { get; private set; }
        public string ClassGuid { get; private set; }
        public string Description { get; private set; }
        public string DeviceID { get; private set; }
        public string Manufacturer { get; private set; }
        public string Name { get; private set; }
        public string PNPClass { get; private set; }
        public string PNPDeviceID { get; private set; }
        public string PortName { get; private set; }
    }
}
