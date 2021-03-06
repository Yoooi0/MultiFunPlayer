using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class SerialOutputTargetViewModel : ThreadAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        public override string Name => "Serial";
        public override ConnectionStatus Status { get; protected set; }

        public BindableCollection<string> ComPorts { get; set; }
        public string SelectedComPort { get; set; }

        public SerialOutputTargetViewModel(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(shortcutManager, eventAggregator, valueProvider)
        {
            ComPorts = new BindableCollection<string>(SerialPort.GetPortNames());
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
                    _ = DialogHost.Show(new ErrorMessageDialog($"Error when opening serial port:\n\n{e}"));
                    await RefreshPorts().ConfigureAwait(true);
                });

                return;
            }

            try
            {
                var lastSentValues = EnumUtils.ToDictionary<DeviceAxis, float>(_ => float.NaN);
                while (!token.IsCancellationRequested && serialPort?.IsOpen == true)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    var dirtyValues = Values.Where(x => TCode.IsDirty(x.Value, lastSentValues[x.Key]));
                    var commands = TCode.ToString(dirtyValues, (int)interval);
                    if (serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedComPort);
                        serialPort?.Write(commands);
                    }

                    foreach (var (axis, value) in dirtyValues)
                        lastSentValues[axis] = value;

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e) when (e is TimeoutException || e is IOException)
            {
                Logger.Error(e, $"{Name} failed with exception");
                _ = Execute.OnUIThreadAsync(async () =>
                {
                    _ = DialogHost.Show(new ErrorMessageDialog($"{Name} failed with exception:\n\n{e}"));
                    await RefreshPorts().ConfigureAwait(true);
                });
            }
            catch (Exception e) { Logger.Debug(e, $"{Name} failed with exception"); }

            try { serialPort?.Close(); }
            catch (IOException) { }
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                if(SelectedComPort != null)
                    settings[nameof(SelectedComPort)] = new JValue(SelectedComPort);
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue<string>(nameof(SelectedComPort), out var selectedComPort))
                    SelectedComPort = selectedComPort;
            }
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
}
