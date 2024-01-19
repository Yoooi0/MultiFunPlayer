using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.TCode;
using MultiFunPlayer.Shortcut;
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
internal sealed class SerialOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider, IInputProcessorManager inputManager)
    : ThreadAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private CancellationTokenSource _refreshCancellationSource = new();

    public override ConnectionStatus Status { get; protected set; }

    public ObservableConcurrentCollection<SerialPortInfo> SerialPorts { get; set; } = [];
    public SerialPortInfo SelectedSerialPort { get; set; }
    public string SelectedSerialPortDeviceId { get; set; }

    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public int DataBits { get; set; } = 8;
    public Handshake Handshake { get; set; } = Handshake.None;
    public bool DtrEnable { get; set; } = true;
    public bool RtsEnable { get; set; } = true;
    public int ReadTimeout { get; set; } = 250;
    public int WriteTimeout { get; set; } = 250;
    public int WriteBufferSize { get; set; } = 2048;
    public int ReadBufferSize { get; set; } = 4096;

    public IReadOnlyCollection<int> AvailableBaudRates { get; } = [50, 75, 110, 134, 150, 200, 300, 600, 1200, 1800, 2400, 4800, 9600, 19200, 28800, 38400, 57600, 76800, 115200, 230400, 460800, 576000, 921600];

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new TCodeThreadFixedUpdateContext(),
        DeviceAxisUpdateType.PolledUpdate => new ThreadPolledUpdateContext(),
        _ => null,
    };

    protected override void OnInitialActivate()
    {
        base.OnInitialActivate();
        _ = RefreshPorts();
    }

    public bool CanChangePort => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public bool IsRefreshBusy { get; set; }
    public bool CanRefreshPorts => !IsRefreshBusy && !IsConnectBusy && !IsConnected;

    private int _isRefreshingFlag;
    public async Task RefreshPorts()
    {
        if (Interlocked.CompareExchange(ref _isRefreshingFlag, 1, 0) != 0)
            return;

        try
        {
            var token = _refreshCancellationSource.Token;
            token.ThrowIfCancellationRequested();

            IsRefreshBusy = true;
            await DoRefreshPorts(token);
        }
        catch (Exception e)
        {
            Logger.Warn(e, $"{Identifier} port refresh failed with exception");
        }
        finally
        {
            Interlocked.Decrement(ref _isRefreshingFlag);
            IsRefreshBusy = false;
        }

        async Task DoRefreshPorts(CancellationToken token)
        {
            await Task.Delay(250, token);

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
            await using (token.Register(() => taskCompletion.TrySetCanceled()))
                await taskCompletion.Task.WaitAsync(token);

            var lastSelectedDeviceId = SelectedSerialPortDeviceId;
            SerialPorts.RemoveRange(SerialPorts.Except(serialPorts).ToList());
            SerialPorts.AddRange(serialPorts.Except(SerialPorts).ToList());

            SelectSerialPortByDeviceId(lastSelectedDeviceId);

            await Task.Delay(250, token);
        }
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

    protected override async ValueTask<bool> OnConnectingAsync()
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

            serialPort = CreateSerialPort();
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

            using var _ = inputManager.Register<TCodeInputProcessor>(out var tcodeInputProcessor);

            var receiveBuffer = new SplittingStringBuffer('\n');
            var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            var lastSentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate<TCodeThreadFixedUpdateContext>(() => !token.IsCancellationRequested && serialPort.IsOpen, (context, elapsed) =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                GetValues(currentValues);

                if (serialPort.IsOpen && serialPort.BytesToRead > 0)
                {
                    var receivedString = serialPort.ReadExisting();
                    Logger.Debug("Received \"{0}\" from \"{1}\"", receivedString, SelectedSerialPortDeviceId);

                    receiveBuffer.Push(receivedString);
                    foreach (var command in receiveBuffer.Consume())
                        tcodeInputProcessor.Parse(command);
                }

                var values = context.SendDirtyValuesOnly ? currentValues.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSentValues[x.Key])) : currentValues;
                values = values.Where(x => AxisSettings[x.Key].Enabled);

                var commands = context.OffloadElapsedTime ? DeviceAxis.ToString(values) : DeviceAxis.ToString(values, elapsed * 1000);
                if (serialPort.IsOpen && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedSerialPortDeviceId);
                    serialPort.Write(commands);
                    lastSentValues.Merge(values);
                }
            });
        }
        catch (Exception e) when (e is TimeoutException || e is IOException)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
        catch (Exception e) { Logger.Error(e, $"{Identifier} failed with exception"); }

        try { serialPort?.Dispose(); }
        catch { }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(SelectedSerialPort)] = SelectedSerialPortDeviceId;
            settings[nameof(BaudRate)] = BaudRate;
            settings[nameof(Parity)] = JToken.FromObject(Parity);
            settings[nameof(StopBits)] = JToken.FromObject(StopBits);
            settings[nameof(DataBits)] = DataBits;
            settings[nameof(Handshake)] = JToken.FromObject(Handshake);
            settings[nameof(DtrEnable)] = DtrEnable;
            settings[nameof(RtsEnable)] = RtsEnable;
            settings[nameof(ReadTimeout)] = ReadTimeout;
            settings[nameof(WriteTimeout)] = WriteTimeout;
            settings[nameof(WriteBufferSize)] = WriteBufferSize;
            settings[nameof(ReadBufferSize)] = ReadBufferSize;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedSerialPort), out var selectedSerialPort))
                SelectSerialPortByDeviceId(selectedSerialPort);

            if (settings.TryGetValue<int>(nameof(BaudRate), out var baudRate)) BaudRate = baudRate;
            if (settings.TryGetValue<Parity>(nameof(Parity), out var parity)) Parity = parity;
            if (settings.TryGetValue<StopBits>(nameof(StopBits), out var stopBits)) StopBits = stopBits;
            if (settings.TryGetValue<int>(nameof(DataBits), out var dataBits)) DataBits = dataBits;
            if (settings.TryGetValue<Handshake>(nameof(Handshake), out var handshake)) Handshake = handshake;
            if (settings.TryGetValue<bool>(nameof(DtrEnable), out var dtrEnable)) DtrEnable = dtrEnable;
            if (settings.TryGetValue<bool>(nameof(RtsEnable), out var rtsEnable)) RtsEnable = rtsEnable;
            if (settings.TryGetValue<int>(nameof(ReadTimeout), out var readTimeout)) ReadTimeout = readTimeout;
            if (settings.TryGetValue<int>(nameof(WriteTimeout), out var writeTimeout)) WriteTimeout = writeTimeout;
            if (settings.TryGetValue<int>(nameof(WriteBufferSize), out var writeBufferSize)) WriteBufferSize = writeBufferSize;
            if (settings.TryGetValue<int>(nameof(ReadBufferSize), out var readBufferSize)) ReadBufferSize = readBufferSize;
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ComPort
        s.RegisterAction<string>($"{Identifier}::SerialPort::Set", s => s.WithLabel("Device ID"), deviceId => SelectSerialPortByDeviceId(deviceId));
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

            using var serialPort = CreateSerialPort();
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

        base.Dispose(disposing);
    }

    private SerialPort CreateSerialPort() => new()
    {
        PortName = SelectedSerialPort.PortName,
        BaudRate = BaudRate,
        Parity = Parity,
        StopBits = StopBits,
        DataBits = DataBits,
        Handshake = Handshake,
        DtrEnable = DtrEnable,
        RtsEnable = RtsEnable,
        ReadTimeout = ReadTimeout,
        WriteTimeout = WriteTimeout,
        WriteBufferSize = WriteBufferSize,
        ReadBufferSize = ReadBufferSize,
    };

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
