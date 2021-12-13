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
using System.IO.Ports;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Serial")]
public class SerialOutputTargetViewModel : ThreadAbstractOutputTarget
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public ObservableConcurrentCollection<string> ComPorts { get; set; }
    public string SelectedComPort { get; set; }

    public SerialOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(shortcutManager, eventAggregator, valueProvider)
    {
        ComPorts = new ObservableConcurrentCollection<string>(SerialPort.GetPortNames());
    }

    public bool CanChangePort => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public bool IsRefreshBusy { get; set; }
    public bool CanRefreshPorts => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
    public async Task RefreshPorts()
    {
        IsRefreshBusy = true;
        await Task.Delay(750).ConfigureAwait(true);

        var lastSelected = SelectedComPort;
        ComPorts.Clear();
        try
        {
            ComPorts.AddRange(SerialPort.GetPortNames());
        }
        catch { }

        SelectedComPort = lastSelected;
        await Task.Delay(250).ConfigureAwait(true);
        IsRefreshBusy = false;
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SelectedComPort != null;

    public override async Task ConnectAsync()
    {
        if (!ComPorts.Contains(SelectedComPort))
            await RefreshPorts();

        await base.ConnectAsync();
    }

    protected override void Run(CancellationToken token)
    {
        var serialPort = default(SerialPort);

        try
        {
            Logger.Info("Connecting to {0}", SelectedComPort);

            serialPort = new SerialPort(SelectedComPort, 115200)
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
                UpdateValues();

                var dirtyValues = Values.Where(x => DeviceAxis.IsDirty(x.Value, lastSentValues[x.Key]));
                var commands = DeviceAxis.ToString(dirtyValues, UpdateInterval);
                if (serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                {
                    Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedComPort);
                    serialPort?.Write(commands);
                }

                foreach (var (axis, value) in dirtyValues)
                    lastSentValues[axis] = value;

                Sleep(stopwatch);
                stopwatch.Restart();
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
            settings[nameof(SelectedComPort)] = new JValue(SelectedComPort);
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<string>(nameof(SelectedComPort), out var selectedComPort))
                SelectedComPort = selectedComPort;
        }
    }

    protected override void RegisterShortcuts(IShortcutManager s)
    {
        base.RegisterShortcuts(s);

        #region ComPort
        s.RegisterAction($"{Name}::ComPort::Set", b => b.WithSetting<string>(s => s.WithLabel("Com port")).WithCallback((_, comPort) => SelectedComPort = comPort));
        #endregion
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token)
    {
        try
        {
            await RefreshPorts();
            if (!ComPorts.Contains(SelectedComPort))
                return await ValueTask.FromResult(false);

            using var serialPort = new SerialPort(SelectedComPort, 115200)
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
}
