using Buttplug;
using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using NLog;
using PropertyChanged;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public static class E
    {
        public static string ToString(this ButtplugClientDevice device)
            => device.Name;
    }

    public class ButtplugOutputTargetViewModel : AsyncAbstractOutputTarget
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly List<ServerMessage.Types.MessageAttributeType> _supportedMessages = new()
        {
            ServerMessage.Types.MessageAttributeType.LinearCmd,
            ServerMessage.Types.MessageAttributeType.RotateCmd,
            ServerMessage.Types.MessageAttributeType.VibrateCmd
        };

        public override string Name => "Buttplug.io";
        public override OutputTargetStatus Status { get; protected set; }
        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 12345);
        public BindableCollection<ButtplugClientDevice> AvailableDevices { get; protected set; }

        [DependsOn(nameof(SelectedDevice))]
        public BindableCollection<ServerMessage.Types.MessageAttributeType> AvailableMessageTypes
            => SelectedDevice != null ? new(SelectedDevice.AllowedMessages.Keys.Join(_supportedMessages, x => x, x => x, (x, _) => x)) : null;

        [DependsOn(nameof(SelectedDevice), nameof(AvailableMessageTypes))]
        public BindableCollection<uint> AvailableFeatureIndices
        {
            get
            {
                if (SelectedDevice == null || SelectedMessageType == null)
                    return null;

                var indices = Enumerable.Range(0, (int)SelectedDevice.AllowedMessages[SelectedMessageType.Value].FeatureCount).Select(x => (uint)x);
                var usedIndices = DeviceSettings.Where(s => s.Device == SelectedDevice && s.MessageType == SelectedMessageType).Select(s => s.FeatureIndex);
                var allowedIndices = indices.Except(usedIndices);
                if (!allowedIndices.Any())
                    return null;

                return new(allowedIndices);
            }
        }

        public ButtplugClientDevice SelectedDevice { get; set; }
        public DeviceAxis? SelectedDeviceAxis { get; set; }
        public ServerMessage.Types.MessageAttributeType? SelectedMessageType { get; set; }
        public uint? SelectedFeatureIndex { get; set; }
        public bool CanAddSelected => SelectedDevice != null && SelectedDeviceAxis != null && SelectedMessageType != null && SelectedFeatureIndex != null;

        public BindableCollection<ButtplugClientDeviceSettings> DeviceSettings { get; protected set; }

        public ButtplugOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider)
        {
            AvailableDevices = new BindableCollection<ButtplugClientDevice>();
            DeviceSettings = new BindableCollection<ButtplugClientDeviceSettings>();
            UpdateRate = 20;

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

            ButtplugFFILog.LogMessage += (_, message) => Logger.Info(message.Trim());
            ButtplugFFILog.SetLogOptions(logLevel, true);
        }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override async Task RunAsync(CancellationToken token)
        {
            void OnDeviceRemoved(ButtplugClientDevice device)
            {
                Logger.Info($"Device removed: \"{device.Name}\"");
                AvailableDevices.Remove(device);
                DeviceSettings.RemoveRange(DeviceSettings.Where(s => s.Device == device).ToList());
            }

            void OnDeviceAdded(ButtplugClientDevice device)
            {
                Logger.Info($"Device added: \"{device.Name}\"");
                AvailableDevices.Add(device);
            }

            using var client = new ButtplugClient(nameof(MultiFunPlayer));
            client.DeviceAdded += (_, e) => OnDeviceAdded(e.Device);
            client.DeviceRemoved += (_, e) => OnDeviceRemoved(e.Device);
            client.ErrorReceived += (_, e) => Logger.Debug(e.Exception);

            try
            {
                Logger.Info("Connecting to {0}", $"ws://{Endpoint}");
                await client.ConnectAsync(new ButtplugWebsocketConnectorOptions(new Uri($"ws://{Endpoint}"))).WithCancellation(token);
                Status = OutputTargetStatus.Connected;
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Error when connecting to server");
                if (client.Connected)
                    await client.DisconnectAsync();

                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when connecting to server:\n\n{e}")));
                return;
            }

            try
            {
                _ = Task.Factory.StartNew(async () =>
                {
                    try
                    {
                        if (client.IsScanning)
                            await client.StopScanningAsync().WithCancellation(token);
                    }
                    catch (ButtplugException) { }

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await client.StartScanningAsync().WithCancellation(token);
                            await Task.Delay(5000);
                            await client.StopScanningAsync().WithCancellation(token);
                            await Task.Delay(10000);
                        }
                        catch (ButtplugException) { }
                    }
                });

                var lastSentValues = EnumUtils.ToDictionary<DeviceAxis, float>(_ => float.PositiveInfinity);
                while (!token.IsCancellationRequested && client.Connected)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    var validSettings = DeviceSettings.Where(s => float.IsFinite(Values[s.SourceAxis]));

                    try
                    {
                        await Task.WhenAll(validSettings.GroupBy(m => m.Device).SelectMany(deviceGroup =>
                        {
                            var device = deviceGroup.Key;
                            return deviceGroup.GroupBy(m => m.MessageType).Select(typeGroup =>
                            {
                                var type = typeGroup.Key;
                                if (type == ServerMessage.Types.MessageAttributeType.VibrateCmd)
                                    return device.SendVibrateCmd(typeGroup.ToDictionary(m => m.FeatureIndex,
                                                                                        m => (double)Values[m.SourceAxis]));

                                if (type == ServerMessage.Types.MessageAttributeType.LinearCmd)
                                    return device.SendLinearCmd(typeGroup.ToDictionary(m => m.FeatureIndex,
                                                                                       m => ((uint)interval, (double)Values[m.SourceAxis])));

                                if (type == ServerMessage.Types.MessageAttributeType.RotateCmd)
                                    return device.SendRotateCmd(typeGroup.ToDictionary(m => m.FeatureIndex,
                                                                                       m => (Math.Clamp(Math.Abs(Values[m.SourceAxis] - 0.5) / 0.5, 0, 1), Values[m.SourceAxis] > 0.5)));

                                return Task.CompletedTask;
                            });
                        }));
                    }
                    catch (ButtplugDeviceException) { }

                    foreach(var group in validSettings.GroupBy(x => x.SourceAxis))
                        lastSentValues[group.Key] = Values[group.Key];

                    await Task.Delay((int)interval, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Logger.Error(e, "Unhandled error");
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}")));
            }

            if (client.Connected)
                await client.DisconnectAsync();

            DeviceSettings.Clear();
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
        {
            if (type == AppSettingsMessageType.Saving)
            {
                if (Endpoint != null)
                    settings[nameof(Endpoint)] = new JValue(Endpoint.ToString());
            }
            else if (type == AppSettingsMessageType.Loading)
            {
                if (settings.TryGetValue(nameof(Endpoint), out var endpointToken) && IPEndPoint.TryParse(endpointToken.ToObject<string>(), out var endpoint))
                    Endpoint = endpoint;
            }
        }

        public void OnSettingsAdd()
        {
            DeviceSettings.Add(new()
            {
                Device = SelectedDevice,
                SourceAxis = SelectedDeviceAxis.Value,
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

    public class ButtplugClientDeviceSettings : PropertyChangedBase
    {
        public ButtplugClientDevice Device { get; set; }
        public DeviceAxis SourceAxis { get; set; }
        public ServerMessage.Types.MessageAttributeType MessageType { get; set; }
        public uint FeatureIndex { get; set; }
    }
}