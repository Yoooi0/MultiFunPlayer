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
    public abstract class AbstractOutputTarget : Screen, IHandle<AppSettingsMessage>, IOutputTarget
    {
        private readonly IDeviceAxisValueProvider _valueProvider;
        private readonly AsyncManualResetEvent _statusEvent;

        public abstract string Name { get; }
        [SuppressPropertyChangedWarnings] public abstract ConnectionStatus Status { get; protected set; }
        public bool ContentVisible { get; set; } = false;
        public bool AutoConnectEnabled { get; set; } = false;

        public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
        public int UpdateRate { get; set; }
        protected Dictionary<DeviceAxis, float> Values { get; }

        protected AbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            _statusEvent = new AsyncManualResetEvent();
            eventAggregator.Subscribe(this);
            _valueProvider = valueProvider;

            Values = EnumUtils.ToDictionary<DeviceAxis, float>(axis => axis.DefaultValue());
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(EnumUtils.ToDictionary<DeviceAxis, DeviceAxisSettings>(_ => new DeviceAxisSettings()));
            UpdateRate = 60;

            PropertyChanged += (s, e) =>
            {
                if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                    _statusEvent.Reset();
            };
        }

        public abstract Task ConnectAsync();
        public abstract Task DisconnectAsync();

        public async virtual ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false);
        public async virtual ValueTask<bool> CanConnectAsyncWithStatus(CancellationToken token)
        {
            if (Status != ConnectionStatus.Disconnected)
                return await ValueTask.FromResult(false);

            Status = ConnectionStatus.Connecting;
            await Task.Delay(100, token);
            var result = await CanConnectAsync(token);
            Status = ConnectionStatus.Disconnected;

            return result;
        }

        public async Task WaitForStatus(IEnumerable<ConnectionStatus> statuses, CancellationToken token)
        {
            while (!statuses.Contains(Status))
                await _statusEvent.WaitAsync(token);
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

                if (settings.TryGetValue<int>(nameof(UpdateRate), out var updateRate))
                    UpdateRate = updateRate;
                if (settings.TryGetValue<Dictionary<DeviceAxis, DeviceAxisSettings>>(nameof(AxisSettings), out var axisSettingsMap))
                    foreach (var (axis, axisSettings) in axisSettingsMap)
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

        public override async Task ConnectAsync()
        {
            if (Status != ConnectionStatus.Disconnected)
                return;

            Status = ConnectionStatus.Connecting;
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

            await Task.CompletedTask;
        }

        public override async Task DisconnectAsync()
        {
            if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
                return;

            Status = ConnectionStatus.Disconnecting;

            _cancellationSource?.Cancel();
            _thread?.Join();

            await Task.Delay(250);
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _thread = null;

            Status = ConnectionStatus.Disconnected;
        }

        protected override async void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            await DisconnectAsync();
        }
    }

    public abstract class AsyncAbstractOutputTarget : AbstractOutputTarget
    {
        private CancellationTokenSource _cancellationSource;
        private Task _task;

        protected AsyncAbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(eventAggregator, valueProvider) { }

        protected abstract Task RunAsync(CancellationToken token);

        public override async Task ConnectAsync()
        {
            if (Status != ConnectionStatus.Disconnected)
                return;

            Status = ConnectionStatus.Connecting;
            _cancellationSource = new CancellationTokenSource();
            _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
                _cancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
                .Unwrap();
            _ = _task.ContinueWith(_ => Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true))).Unwrap();

            await Task.CompletedTask;
        }

        public override async Task DisconnectAsync()
        {
            if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
                return;

            Status = ConnectionStatus.Disconnecting;

            _cancellationSource?.Cancel();

            if (_task != null)
                await _task;

            await Task.Delay(250);
            _cancellationSource?.Dispose();

            _cancellationSource = null;
            _task = null;

            Status = ConnectionStatus.Disconnected;
        }

        protected override async void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            await DisconnectAsync();
        }
    }
}