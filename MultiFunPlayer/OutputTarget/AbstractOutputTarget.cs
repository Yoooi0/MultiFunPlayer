using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
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

        protected AbstractOutputTarget(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            _statusEvent = new AsyncManualResetEvent();
            eventAggregator.Subscribe(this);
            _valueProvider = valueProvider;

            Values = DeviceAxis.All.ToDictionary(a => a, a => a.DefaultValue);
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(DeviceAxis.All.ToDictionary(a => a, _ => new DeviceAxisSettings()));
            UpdateRate = 60;

            PropertyChanged += (s, e) =>
            {
                if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                    _statusEvent.Reset();
            };

            RegisterShortcuts(shortcutManager);
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

        protected virtual float CoerceProviderValue(DeviceAxis axis, float value)
        {
            if (!float.IsFinite(value))
                return axis.DefaultValue;

            return value;
        }

        protected void UpdateValues()
        {
            foreach (var axis in DeviceAxis.All)
            {
                var value = CoerceProviderValue(axis, _valueProvider?.GetValue(axis) ?? float.NaN);
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
                settings[nameof(AutoConnectEnabled)] = new JValue(AutoConnectEnabled);

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
                if (settings.TryGetValue<bool>(nameof(AutoConnectEnabled), out var autoConnectEnabled))
                    AutoConnectEnabled = autoConnectEnabled;

                HandleSettings(settings, message.Type);
            }
        }

        protected virtual void RegisterShortcuts(IShortcutManager s)
        {
            static void SetMinimum(DeviceAxisSettings settings, float value) => settings.Minimum = MathUtils.Clamp(value, 0, settings.Maximum - 1);
            static void SetMaximum(DeviceAxisSettings settings, float value) => settings.Maximum = MathUtils.Clamp(value, settings.Minimum + 1, 100);

            static void OffsetMiddle(DeviceAxisSettings settings, float offset)
            {
                if (offset > 0 && settings.Maximum + offset > 100)
                    offset = Math.Min(offset, 100 - settings.Maximum);
                else if (offset < 0 && settings.Minimum + offset < 0)
                    offset = Math.Max(offset, 0 - settings.Minimum);

                settings.Minimum = MathUtils.Clamp(settings.Minimum + offset, 0, 99);
                settings.Maximum = MathUtils.Clamp(settings.Maximum + offset, 1, 100);
            }

            static void OffsetSize(DeviceAxisSettings settings, float offset)
            {
                var middle = (settings.Maximum + settings.Minimum) / 2.0f;
                var newRange = MathUtils.Clamp(settings.Maximum - settings.Minimum + offset, 1, 100);
                var newMaximum = middle + newRange / 2;
                var newMinimum = middle - newRange / 2;

                if (newMaximum > 100)
                    newMinimum -= newMaximum - 100;
                if (newMinimum < 0)
                    newMaximum += 0 - newMinimum;

                settings.Minimum = MathUtils.Clamp(newMinimum, 0, 99);
                settings.Maximum = MathUtils.Clamp(newMaximum, 1, 100);
            }

            #region UpdateRate
            s.RegisterAction<int>($"{Name}::UpdateRate::Set", "Update rate", (_, updateRate) => UpdateRate = updateRate);
            #endregion

            #region AutoConnectEnabled
            s.RegisterAction<bool>($"{Name}::AutoConnectEnabled::Set", "Enable auto connect", (_, enabled) => AutoConnectEnabled = enabled);
            s.RegisterAction($"{Name}::AutoConnectEnabled::Toggle", (_) => AutoConnectEnabled = !AutoConnectEnabled);
            #endregion

            #region Axis::Range::Minimum
            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Minimum::Offset", "Target axis", "Value offset", (_, axis, offset) =>
            {
                if (axis != null)
                    SetMinimum(AxisSettings[axis], AxisSettings[axis].Minimum + offset);
            });

            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Minimum::Set", "Target axis", "Value", (_, axis, value) =>
            {
                if (axis != null)
                    SetMinimum(AxisSettings[axis], value);
            });

            s.RegisterAction<DeviceAxis>($"{Name}::Axis::Range::Minimum::Drive", "Target axis", (gesture, axis) =>
            {
                if (gesture is not IAxisInputGesture axisGesture) return;
                if (axis != null)
                    SetMinimum(AxisSettings[axis], AxisSettings[axis].Minimum + axisGesture.Delta * 100);
            });
            #endregion

            #region Axis::Range::Maximum
            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Maximum::Offset", "Target axis", "Value offset", (_, axis, offset) =>
            {
                if (axis != null)
                    SetMaximum(AxisSettings[axis], AxisSettings[axis].Maximum + offset);
            });

            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Maximum::Set", "Target axis", "Value", (_, axis, value) =>
            {
                if (axis != null)
                    SetMaximum(AxisSettings[axis], value);
            });

            s.RegisterAction<DeviceAxis>($"{Name}::Axis::Range::Maximum::Drive", "Target axis", (gesture, axis) =>
            {
                if (gesture is not IAxisInputGesture axisGesture) return;
                if (axis != null)
                    SetMaximum(AxisSettings[axis], AxisSettings[axis].Maximum + axisGesture.Delta * 100);
            });
            #endregion

            #region Axis::Range::Middle
            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Middle::Offset", "Target axis", "Value offset", (_, axis, offset) =>
            {
                if (axis != null)
                    OffsetMiddle(AxisSettings[axis], offset);
            });

            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Middle::Set", "Target axis", "Value", (_, axis, value) =>
            {
                if (axis != null)
                    OffsetMiddle(AxisSettings[axis], value - (AxisSettings[axis].Maximum - AxisSettings[axis].Minimum) / 2);
            });

            s.RegisterAction<DeviceAxis>($"{Name}::Axis::Range::Middle::Drive", "Target axis", (gesture, axis) =>
            {
                if (gesture is not IAxisInputGesture axisGesture) return;
                if (axis != null)
                    OffsetMiddle(AxisSettings[axis], axisGesture.Delta * 100);
            });
            #endregion

            #region Axis::Range::Size
            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Size::Offset", "Target axis", "Value offset", (_, axis, offset) =>
            {
                if (axis != null)
                    OffsetSize(AxisSettings[axis], offset);
            });

            s.RegisterAction<DeviceAxis, int>($"{Name}::Axis::Range::Size::Set", "Target axis", "Value", (_, axis, value) =>
            {
                if (axis != null)
                    OffsetSize(AxisSettings[axis], value - (AxisSettings[axis].Maximum - AxisSettings[axis].Minimum));
            });

            s.RegisterAction<DeviceAxis>($"{Name}::Axis::Range::Size::Drive", "Target axis", (gesture, axis) =>
            {
                if (gesture is not IAxisInputGesture axisGesture) return;
                if (axis != null)
                    OffsetSize(AxisSettings[axis], axisGesture.Delta * 100);
            });
            #endregion
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

        protected ThreadAbstractOutputTarget(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(shortcutManager, eventAggregator, valueProvider) { }

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

        protected AsyncAbstractOutputTarget(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
            : base(shortcutManager, eventAggregator, valueProvider) { }

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