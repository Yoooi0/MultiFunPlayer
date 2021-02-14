using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.OutputTarget
{
    public abstract class AbstractOutputTarget : Screen, IHandle<AppSettingsMessage>, IDisposable, IOutputTarget
    {
        private CancellationTokenSource _cancellationSource;
        private Thread _thread;

        public abstract string Name { get; }
        [SuppressPropertyChangedWarnings] public abstract OutputTargetStatus Status { get; protected set; }

        public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
        public int UpdateRate { get; set; }
        protected IDeviceAxisValueProvider ValueProvider { get; }

        protected AbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            eventAggregator.Subscribe(this);
            ValueProvider = valueProvider;

            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new DeviceAxisSettings()));
            UpdateRate = 60;
        }

        protected abstract void Run(CancellationToken token);

        public async Task ToggleConnectAsync()
        {
            if (Status == OutputTargetStatus.Connected || Status == OutputTargetStatus.Connecting)
                await DisconnectAsync().ConfigureAwait(true);
            else
                await ConnectAsync().ConfigureAwait(true);
        }

        protected virtual async Task ConnectAsync()
        {
            if (Status != OutputTargetStatus.Disconnected)
                return;

            Status = OutputTargetStatus.Connecting;
            await Task.Delay(1000).ConfigureAwait(true);

            _cancellationSource = new CancellationTokenSource();
            _thread = new Thread(() =>
            {
                Run(_cancellationSource.Token);
                _ = Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(false));
            })
            {
                IsBackground = true
            };
            _thread.Start();
        }

        protected virtual async Task DisconnectAsync()
        {
            if (Status == OutputTargetStatus.Disconnected || Status == OutputTargetStatus.Disconnecting)
                return;

            Status = OutputTargetStatus.Disconnecting;
            Dispose(disposing: false);
            await Task.Delay(1000).ConfigureAwait(false);
            Status = OutputTargetStatus.Disconnected;
        }

        protected abstract void HandleSettings(JObject settings, AppSettingsMessageType type);
        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("OutputTarget", Name)
                 || !message.Settings.TryGetObject(out var settings, "OutputTarget", Name))
                    return;

                settings[nameof(UpdateRate)] = new JValue(UpdateRate);
                settings[nameof(AxisSettings)] = JObject.FromObject(AxisSettings);

                HandleSettings(settings, message.Type);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "OutputTarget", Name))
                    return;

                if (settings.TryGetValue(nameof(UpdateRate), out var updateRateToken))
                    UpdateRate = updateRateToken.ToObject<int>();
                if (settings.TryGetValue(nameof(AxisSettings), out var axisSettingsToken))
                    foreach (var (axis, axisSettings) in axisSettingsToken.ToObject<Dictionary<DeviceAxis, DeviceAxisSettings>>())
                        AxisSettings[axis] = axisSettings;

                HandleSettings(settings, message.Type);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            _cancellationSource?.Cancel();
            _thread?.Join();
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _thread = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}