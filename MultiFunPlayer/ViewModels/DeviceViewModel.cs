using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class DeviceViewModel : PropertyChangedBase, IHandle<AppSettingsMessage>, IDisposable
    {
        private readonly IDeviceAxisValueProvider _valueProvider;
        private CancellationTokenSource _cancellationSource;
        private Thread _deviceThread;
        private SerialPort _serialPort;

        public BindableCollection<ComPortModel> ComPorts { get; set; }

        public ObservableConcurrentDictionary<DeviceAxis, AxisSettingsModel> AxisSettings { get; set; }
        public ComPortModel SelectedComPort { get; set; }
        public int UpdateRate { get; set; }

        public DeviceViewModel(IEventAggregator eventAggregator, ValuesViewModel valueProvider)
        {
            eventAggregator.Subscribe(this);
            _valueProvider = valueProvider;

            ComPorts = new BindableCollection<ComPortModel>(SerialPort.GetPortNames().Select(p => new ComPortModel(p)));
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, AxisSettingsModel>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisSettingsModel()));
            UpdateRate = 60;
        }

        public bool IsConnected { get; set; }
        public bool IsBusy { get; set; }
        public bool CanToggleConnect => !IsBusy && SelectedComPort != null;
        public async Task ToggleConnect()
        {
            IsBusy = true;

            if (IsConnected)
            {
                await Disconnect();
                IsConnected = false;
            }
            else
            {
                IsConnected = await Connect();
            }

            IsBusy = false;
        }

        public async Task<bool> Connect()
        {
            if (SelectedComPort == null)
                return false;

            await Task.Delay(1000);

            try
            {
                _serialPort = new SerialPort(SelectedComPort.Name, 115200)
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
            _cancellationSource?.Cancel();
            _deviceThread?.Join();

            if (_serialPort?.IsOpen == true)
                _serialPort?.Close();
            _cancellationSource?.Dispose();

            await Task.Delay(1000);

            _cancellationSource = null;
            _deviceThread = null;
            _serialPort = null;
        }

        private void UpdateDevice(object state)
        {
            var token = (CancellationToken)state;
            var sb = new StringBuilder(256);
            //var stopwatch = new Stopwatch();

            //stopwatch.Start();
            while (!token.IsCancellationRequested)
            {
                sb.Clear();
                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                {
                    var value = _valueProvider?.GetValue(axis) ?? float.NaN;
                    if (!float.IsFinite(value))
                        value = axis.DefaultValue();

                    if (AxisSettings.TryGetValue(axis, out var axisSettings))
                        value = MathUtils.Lerp(axisSettings.Minimum / 100.0f, axisSettings.Maximum / 100.0f, value);

                    sb.Append(axis)
                      .AppendFormat("{0:000}", value * 999)
                      .Append(' ');
                }

                var commands = sb.ToString().Trim();
                if (_serialPort?.IsOpen == true && !string.IsNullOrWhiteSpace(commands))
                    _serialPort?.WriteLine(commands);

                //stopwatch.PreciseSleep((float)Math.Round(1000.0f / UpdateRate), token);
                Thread.Sleep((int)Math.Max(1, Math.Floor(Math.Round(1000.0f / UpdateRate))));
            }
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                var settings = new JObject
                {
                    { nameof(UpdateRate), new JValue(UpdateRate) },
                    { nameof(SelectedComPort), new JValue(SelectedComPort?.Name) },
                    { nameof(AxisSettings), JObject.FromObject(AxisSettings) }
                };

                message.Settings.Add("Device", settings);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.ContainsKey("Device"))
                    return;

                var settings = message.Settings["Device"] as JObject;
                if (settings.TryGetValue(nameof(UpdateRate), out var updateRateToken))
                    UpdateRate = updateRateToken.ToObject<int>();
                if (settings.TryGetValue(nameof(SelectedComPort), out var selectedComPortToken))
                    SelectedComPort = ComPorts.FirstOrDefault(x => string.Equals(x.Name, selectedComPortToken.ToObject<string>(), StringComparison.OrdinalIgnoreCase));
                if (settings.TryGetValue(nameof(AxisSettings), out var axisSettingsToken))
                    AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, AxisSettingsModel>(axisSettingsToken.ToObject<Dictionary<DeviceAxis, AxisSettingsModel>>());
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            Disconnect().Wait();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class AxisSettingsModel
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; }

        public AxisSettingsModel()
        {
            Minimum = 0;
            Maximum = 100;
        }
    }

    public class ComPortModel
    {
        public string Name { get; }
        public string Description { get; }

        public ComPortModel(string name) : this(name, null) { }

        public ComPortModel(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public override string ToString() => Description != null ? $"{Name} ({Description})" : Name;
    }
}
