using Buttplug;
using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Controls;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget.ViewModels
{
    public class ButtplugOutputTargetViewModel : AsyncAbstractOutputTarget
    {
        public override string Name => "Buttplug.io";
        public override OutputTargetStatus Status { get; protected set; }
        public IPEndPoint Endpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 12345);
        public ObservableConcurrentDictionary<string, ButtplugClientDeviceSettings> DeviceSettings { get; protected set; }

        public ButtplugOutputTargetViewModel(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider)
        {
            DeviceSettings = new ObservableConcurrentDictionary<string, ButtplugClientDeviceSettings>();
            UpdateRate = 20;
        }

        public bool IsConnected => Status == OutputTargetStatus.Connected;
        public bool IsConnectBusy => Status == OutputTargetStatus.Connecting || Status == OutputTargetStatus.Disconnecting;
        public bool CanToggleConnect => !IsConnectBusy;

        protected override async Task RunAsync(CancellationToken token)
        {
            void OnDeviceRemoved(ButtplugClientDevice device)
            {
                if (!DeviceSettings.ContainsKey(device.Name))
                    return;

                DeviceSettings.Remove(device.Name);
            }

            void OnDeviceAdded(ButtplugClientDevice device)
            {
                if (!device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd))
                    return;

                DeviceSettings.AddOrUpdate(device.Name, new ButtplugClientDeviceSettings());
            }

            using var client = new ButtplugClient(nameof(MultiFunPlayer));
            client.DeviceAdded += (s, e) => OnDeviceAdded(e.Device);
            client.DeviceRemoved += (s, e) => OnDeviceRemoved(e.Device);
            client.ErrorReceived += (s, e) => Debug.WriteLine(e.Exception.ToString());

            try
            {
                await client.ConnectAsync(new ButtplugWebsocketConnectorOptions(new Uri($"ws://{Endpoint}"))).WithCancellation(token).ConfigureAwait(false);
                Status = OutputTargetStatus.Connected;
            }
            catch (Exception e)
            {
                if (client.Connected)
                    await client.DisconnectAsync().ConfigureAwait(false);

                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Error when connecting to server:\n\n{e}")));
                return;
            }

            try
            {
                try { await client.StopScanningAsync().WithCancellation(token).ConfigureAwait(false); } catch (ButtplugException) { }
                try { await client.StartScanningAsync().WithCancellation(token).ConfigureAwait(false); } catch (ButtplugException) { }

                await Task.Delay(2500, token).ConfigureAwait(false);
                foreach (var device in client.Devices)
                    OnDeviceAdded(device);

                var lastSentValues = EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => float.PositiveInfinity);
                while (!token.IsCancellationRequested && client.Connected)
                {
                    var interval = MathF.Max(1, 1000.0f / UpdateRate);
                    UpdateValues();

                    await Task.WhenAll(client.Devices.Where(d => DeviceSettings.ContainsKey(d.Name)).Select(d =>
                    {
                        var settings = DeviceSettings[d.Name];
                        if (settings.SourceAxis == null)
                            return Task.CompletedTask;

                        var axis = settings.SourceAxis.Value;
                        if (!Values.ContainsKey(axis))
                            return Task.CompletedTask;

                        var value = Values[axis];
                        if (value < 0.02f)
                            value = 0;

                        if (!float.IsFinite(value) || Math.Abs(value - lastSentValues[axis]) < 0.02f)
                            return Task.CompletedTask;

                        lastSentValues[axis] = value;
                        return d.SendVibrateCmd(value);
                    })).ConfigureAwait(false);

                    await Task.Delay((int)interval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _ = Execute.OnUIThreadAsync(() => _ = DialogHost.Show(new ErrorMessageDialog($"Unhandled error:\n\n{e}")));
            }

            if (client.Connected)
                await client.DisconnectAsync().ConfigureAwait(false);
        }

        protected override void HandleSettings(JObject settings, AppSettingsMessageType type) { }
    }

    public class ButtplugClientDeviceSettings
    {
        public DeviceAxis? SourceAxis { get; set; }
    }
}
