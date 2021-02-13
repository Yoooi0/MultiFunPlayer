using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class SerialOutputTargetViewModel : AbstractOutputTarget
    {
        private CancellationTokenSource _cancellationSource;
        private Thread _deviceThread;
        private SerialPort _serialPort;

        public override string Name => "Serial";
        public override OutputTargetStatus Status { get; protected set; }

        public BindableCollection<string> ComPorts { get; set; }
        public string SelectedComPort { get; set; }

        public SerialOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider)
        {
            ComPorts = new BindableCollection<string>(SerialPort.GetPortNames());
        }

        public bool IsRefreshBusy { get; set; }
        public bool CanRefreshPorts => !IsRefreshBusy && !IsConnectBusy && !IsConnected;
        public async Task RefreshPorts()
        {
            IsRefreshBusy = true;
            await Task.Delay(750).ConfigureAwait(true);

            ComPorts.Clear();
            SelectedComPort = null;
            try
            {
                ComPorts.AddRange(SerialPort.GetPortNames());
            }
            catch { }

            await Task.Delay(250).ConfigureAwait(true);
            IsRefreshBusy = false;
        }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy && SelectedComPort != null;

        public async Task ToggleConnect()
        {
            if (IsConnected)
            {
                Status = OutputTargetStatus.Disconnecting;
                await Disconnect().ConfigureAwait(true);
                Status = OutputTargetStatus.Disconnected;
            }
            else
            {
                Status = OutputTargetStatus.Connecting;
                if (await Connect().ConfigureAwait(true))
                    Status = OutputTargetStatus.Connected;
                else
                    Status = OutputTargetStatus.Disconnected;
            }
        }

        public async Task<bool> Connect()
        {
            if (SelectedComPort == null)
                return false;

            await Task.Delay(1000).ConfigureAwait(true);

            try
            {
                _serialPort = new SerialPort(SelectedComPort, 115200)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();
                _serialPort.ReadExisting();
            }
            catch (Exception e)
            {
                if (_serialPort?.IsOpen == true)
                    _serialPort.Close();

                _ = Execute.OnUIThreadAsync(() => DialogHost.Show(new ErrorMessageDialog($"Error when opening serial port:\n\n{e}")));
                return false;
            }

            _cancellationSource = new CancellationTokenSource();
            _deviceThread = new Thread(UpdateDevice)
            {
                IsBackground = true
            };
            _deviceThread.Start(_cancellationSource.Token);

            return true;
        }

        public async Task Disconnect()
        {
            Dispose(disposing: false);
            await Task.Delay(1000).ConfigureAwait(false);
        }

        private void UpdateDevice(object state)
        {
            var token = (CancellationToken)state;
            var sb = new StringBuilder(256);

            try
            {
                var values = EnumUtils.GetValues<DeviceAxis>().ToDictionary(axis => axis, axis => axis.DefaultValue());
                while (!token.IsCancellationRequested)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                    {
                        var value = ValueProvider?.GetValue(axis) ?? float.NaN;
                        if (!float.IsFinite(value))
                            value = axis.DefaultValue();

                        var settings = AxisSettings[axis];
                        values[axis] = MathUtils.Lerp(settings.Minimum / 100f, settings.Maximum / 100f, value);
                    }

                    sb.Clear();
                    foreach (var (axis, value) in values)
                    {
                        sb.Append(axis)
                          .AppendFormat("{0:000}", value * 999)
                          .AppendFormat("I{0}", (int)interval)
                          .Append(' ');
                    }

                    var commands = sb.ToString().Trim();
                    if (_serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                        _serialPort?.WriteLine(commands);

                    Thread.Sleep((int)interval);
                }
            }
            catch (Exception e)
            when (e is TimeoutException || e is IOException)
            {
                _ = Execute.OnUIThreadAsync(async () =>
                {
                    _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error while updating device:\n\n{e}"));
                    if (IsConnected)
                        await ToggleConnect().ConfigureAwait(true);
                    await RefreshPorts().ConfigureAwait(true);
                });
            }
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                settings[nameof(SelectedComPort)] = new JValue(SelectedComPort);
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue(nameof(SelectedComPort), out var selectedComPortToken))
                    SelectedComPort = ComPorts.FirstOrDefault(x => string.Equals(x, selectedComPortToken.ToObject<string>(), StringComparison.OrdinalIgnoreCase));
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _cancellationSource?.Cancel();
            _deviceThread?.Join();

            try
            {
                if (_serialPort?.IsOpen == true)
                    _serialPort?.Close();
            }
            catch (IOException) { }

            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _deviceThread = null;
            _serialPort = null;
        }
    }
}
