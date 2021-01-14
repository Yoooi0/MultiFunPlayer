using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class DeviceViewModel : Screen, IHandle<AppSettingsMessage>, IDisposable
    {
        private readonly IDeviceAxisValueProvider _valueProvider;
        private CancellationTokenSource _cancellationSource;
        private Thread _deviceThread;
        private SerialPort _serialPort;

        public BindableCollection<ComPortModel> ComPorts { get; set; }

        public ObservableConcurrentDictionary<DeviceAxis, AxisSettingsModel> AxisSettings { get; set; }
        public ComPortModel SelectedComPort { get; set; }
        public int UpdateRate { get; set; }

        public DeviceViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            eventAggregator.Subscribe(this);
            _valueProvider = valueProvider;

            ComPorts = new BindableCollection<ComPortModel>(SerialPort.GetPortNames().Select(p => new ComPortModel(p)));
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, AxisSettingsModel>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisSettingsModel()));
            UpdateRate = 60;
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
                ComPorts.AddRange(SerialPort.GetPortNames().Select(p => new ComPortModel(p)));
            }
            catch { }

            await Task.Delay(250).ConfigureAwait(true);
            IsRefreshBusy = false;
        }

        public bool IsConnected { get; set; }
        public bool IsConnectBusy { get; set; }
        public bool CanToggleConnect => !IsConnectBusy && SelectedComPort != null;
        public async Task ToggleConnect()
        {
            IsConnectBusy = true;

            if (IsConnected)
            {
                await Disconnect().ConfigureAwait(true);
                IsConnected = false;
            }
            else
            {
                IsConnected = await Connect().ConfigureAwait(true);
            }

            IsConnectBusy = false;
        }

        public async Task<bool> Connect()
        {
            if (SelectedComPort == null)
                return false;

            await Task.Delay(1000).ConfigureAwait(true);

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
            Dispose(disposing: false);
            await Task.Delay(1000).ConfigureAwait(false);
        }

        private void UpdateDevice(object state)
        {
            var token = (CancellationToken)state;
            var sb = new StringBuilder(256);
            //var stopwatch = new Stopwatch();

            //stopwatch.Start();
            try
            {
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

                    //stopwatch.PreciseSleep(MathF.Round(1000.0f / UpdateRate), token);
                    Thread.Sleep((int)MathF.Max(1, MathF.Floor(MathF.Round(1000.0f / UpdateRate))));
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

                message.Settings["Device"] = settings;
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
