using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class SerialOutputTargetViewModel : ThreadAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        public override string Name => "Serial";
        public override OutputTargetStatus Status { get; protected set; }

        public BindableCollection<string> ComPorts { get; set; }
        public string SelectedComPort { get; set; }

        public SerialOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider)
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

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy && SelectedComPort != null;

        protected override async Task ConnectAsync()
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
                Status = OutputTargetStatus.Connected;
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Error when opening serial port");

                try
                {
                    if (serialPort?.IsOpen == true)
                        serialPort.Close();
                }
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
                var sb = new StringBuilder(256);
                var lastSentValues = EnumUtils.ToDictionary<DeviceAxis, float>(_ => float.NaN);
                while (!token.IsCancellationRequested && serialPort?.IsOpen == true)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    sb.Clear();
                    foreach (var (axis, value) in Values)
                    {
                        if (sb.Length > 0)
                            sb.Append(' ');

                        if (float.IsFinite(lastSentValues[axis]) && MathF.Abs(lastSentValues[axis] - value) * 999 < 1)
                            continue;

                        lastSentValues[axis] = value;
                        sb.Append(axis)
                          .AppendFormat("{0:000}", value * 999)
                          .AppendFormat("I{0}", (int)interval);
                    }
                    sb.AppendLine();

                    var commands = sb.ToString();
                    if (serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                    {
                        Logger.Trace("Sending \"{0}\" to \"{1}\"", commands.Trim(), SelectedComPort);
                        serialPort?.Write(commands);
                    }

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e) when (e is TimeoutException || e is IOException)
            {
                Logger.Error(e, "Unhandled error");
                _ = Execute.OnUIThreadAsync(async () =>
                {
                    _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}"));
                    await RefreshPorts().ConfigureAwait(true);
                });
            }
            catch { }

            try
            {
                if (serialPort?.IsOpen == true)
                    serialPort?.Close();
            }
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
                if (settings.TryGetValue(nameof(SelectedComPort), out var selectedComPortToken))
                    SelectedComPort = selectedComPortToken.ToObject<string>();
            }
        }
    }
}
