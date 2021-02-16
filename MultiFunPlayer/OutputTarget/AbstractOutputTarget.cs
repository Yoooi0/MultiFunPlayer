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
        private readonly IDeviceAxisValueProvider _valueProvider;

        public abstract string Name { get; }
        [SuppressPropertyChangedWarnings] public abstract OutputTargetStatus Status { get; protected set; }

        public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
        public int UpdateRate { get; set; }
        protected Dictionary<DeviceAxis, float> Values { get; }

        protected AbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            eventAggregator.Subscribe(this);
            _valueProvider = valueProvider;

            Values = EnumUtils.GetValues<DeviceAxis>().ToDictionary(axis => axis, axis => axis.DefaultValue());
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new DeviceAxisSettings()));
            UpdateRate = 60;
        }

        public async Task ToggleConnectAsync()
        {
            if (Status == OutputTargetStatus.Connected || Status == OutputTargetStatus.Connecting)
                await DisconnectAsync().ConfigureAwait(true);
            else
                await ConnectAsync().ConfigureAwait(true);
        }

        protected abstract Task ConnectAsync();

        protected virtual async Task DisconnectAsync()
        {
            if (Status == OutputTargetStatus.Disconnected || Status == OutputTargetStatus.Disconnecting)
                return;

            Status = OutputTargetStatus.Disconnecting;
            Dispose(disposing: false);
            await Task.Delay(1000).ConfigureAwait(false);
            Status = OutputTargetStatus.Disconnected;
        }

        protected void UpdateValues()
        {
            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
            {
                var value = _valueProvider?.GetValue(axis) ?? float.NaN;
                if (!float.IsFinite(value))
                    value = axis.DefaultValue();

                var settings = AxisSettings[axis];
                Values[axis] = MathUtils.Lerp(settings.Minimum / 100f, settings.Maximum / 100f, value);
            }

            {
                var settings = AxisSettings[DeviceAxis.L0];
                var value = MathUtils.UnLerp(settings.Minimum / 100f, settings.Maximum / 100f, Values[DeviceAxis.L0]);
                var factor = MathUtils.Map(value, 0.25f, 0.9f, 1f, 0f);
                Values[DeviceAxis.R1] = MathUtils.Lerp(DeviceAxis.R1.DefaultValue(), Values[DeviceAxis.R1], factor);
                Values[DeviceAxis.R2] = MathUtils.Lerp(DeviceAxis.R2.DefaultValue(), Values[DeviceAxis.R2], factor);
            }
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

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public abstract class ThreadAbstractOutputTarget : AbstractOutputTarget
    {
        private CancellationTokenSource _cancellationSource;
        private Thread _thread;

        protected ThreadAbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        protected abstract void Run(CancellationToken token);

        protected override async Task ConnectAsync()
        {
            if (Status != OutputTargetStatus.Disconnected)
                return;

            Status = OutputTargetStatus.Connecting;
            await Task.Delay(1000).ConfigureAwait(true);

            _cancellationSource = new CancellationTokenSource();
            _thread = new Thread(() =>
            {
                Run(_cancellationSource.Token);
                _ = Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true));
            })
            {
                IsBackground = true
            };
            _thread.Start();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _cancellationSource?.Cancel();
            _thread?.Join();
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _thread = null;
        }
    }

    public abstract class AsyncAbstractOutputTarget : AbstractOutputTarget
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        protected AsyncAbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        protected abstract Task RunAsync(CancellationToken token);

        protected override async Task ConnectAsync()
        {
            if (Status != OutputTargetStatus.Disconnected)
                return;

            Status = OutputTargetStatus.Connecting;
            await Task.Delay(1000).ConfigureAwait(true);

            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true))).Unwrap();
        }

        protected override async void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task.ConfigureAwait(false);

            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;
        }
    }
}