using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;

namespace MultiFunPlayer.OutputTarget;

public abstract class AbstractOutputTarget : Screen, IHandle<AppSettingsMessage>, IOutputTarget
{
    private readonly IDeviceAxisValueProvider _valueProvider;
    private readonly AsyncManualResetEvent _statusEvent;
    private float _statsTime;
    private int _statsCount;
    private int _statsJitter = int.MinValue;

    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

    [SuppressPropertyChangedWarnings] public abstract ConnectionStatus Status { get; protected set; }
    public bool ContentVisible { get; set; } = false;
    public bool AutoConnectEnabled { get; set; } = false;

    public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
    public int UpdateInterval { get; set; }
    public virtual int MinimumUpdateInterval { get; } = 3;
    public virtual int MaximumUpdateInterval { get; } = 33;
    public int AverageUpdateRate { get; private set; }
    public int UpdateRateJitter { get; private set; }
    public virtual DoubleCollection UpdateIntervalTicks { 
        get
        {
            var ticks = new DoubleCollection();
            for (var i = MaximumUpdateInterval; i >= MinimumUpdateInterval; i--)
                ticks.Add(i);

            return ticks;
        }
    }

    protected Dictionary<DeviceAxis, float> Values { get; }
    protected IEventAggregator EventAggregator { get; }

    protected AbstractOutputTarget(IShortcutManager shortcutManager, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    {
        _statusEvent = new AsyncManualResetEvent();
        _valueProvider = valueProvider;

        EventAggregator = eventAggregator;
        EventAggregator.Subscribe(this);

        Values = DeviceAxis.All.ToDictionary(a => a, a => a.DefaultValue);
        AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(DeviceAxis.All.ToDictionary(a => a, _ => new DeviceAxisSettings()));
        UpdateInterval = 10;

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

    protected void UpdateStats(Stopwatch stopwatch)
    {
        var elapsed = (float)stopwatch.Elapsed.TotalSeconds;
        _statsTime += elapsed;
        _statsCount++;

        var updateRateDiff = (int)MathF.Round(MathF.Abs(1000f / UpdateInterval - 1 / elapsed));
        _statsJitter = Math.Max(_statsJitter, updateRateDiff);

        if (_statsTime > 0.25f)
        {
            UpdateRateJitter = _statsJitter;
            AverageUpdateRate = (int)MathF.Round(1 / (_statsTime / _statsCount));
            _statsTime = 0;
            _statsCount = 0;
            _statsJitter = int.MinValue;
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

            settings[nameof(UpdateInterval)] = new JValue(UpdateInterval);
            settings[nameof(AxisSettings)] = JObject.FromObject(AxisSettings);
            settings[nameof(AutoConnectEnabled)] = new JValue(AutoConnectEnabled);

            HandleSettings(settings, message.Type);
        }
        else if (message.Type == AppSettingsMessageType.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "OutputTarget", Name))
                return;

            if (settings.TryGetValue<int>(nameof(UpdateInterval), out var updateInterval))
                UpdateInterval = updateInterval;
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
        void UpdateSettings(DeviceAxis axis, Action<DeviceAxisSettings> callback)
        {
            if (axis == null)
                return;

            callback(AxisSettings[axis]);
        }

        static void SetMinimum(DeviceAxisSettings settings, float value, float minimumLimit) => settings.Minimum = MathUtils.Clamp(value, minimumLimit, settings.Maximum - 1);
        static void SetMaximum(DeviceAxisSettings settings, float value, float maximumLimit) => settings.Maximum = MathUtils.Clamp(value, settings.Minimum + 1, maximumLimit);

        static void OffsetMiddle(DeviceAxisSettings settings, float offset, float minimumLimit, float maximumLimit)
        {
            if (offset > 0 && settings.Maximum + offset > maximumLimit)
                offset = Math.Min(offset, maximumLimit - settings.Maximum);
            else if (offset < 0 && settings.Minimum + offset < minimumLimit)
                offset = Math.Max(offset, minimumLimit - settings.Minimum);

            settings.Minimum = MathUtils.Clamp(settings.Minimum + offset, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.Clamp(settings.Maximum + offset, minimumLimit + 1, maximumLimit);
        }

        static void OffsetSize(DeviceAxisSettings settings, float offset, float minimumLimit, float maximumLimit)
        {
            var middle = (settings.Maximum + settings.Minimum) / 2.0f;
            var newRange = MathUtils.Clamp(settings.Maximum - settings.Minimum + offset, 1, maximumLimit - minimumLimit);
            var newMaximum = middle + newRange / 2;
            var newMinimum = middle - newRange / 2;

            if (newMaximum > maximumLimit)
                newMinimum -= newMaximum - maximumLimit;
            if (newMinimum < minimumLimit)
                newMaximum += minimumLimit - newMinimum;

            settings.Minimum = MathUtils.Clamp(newMinimum, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.Clamp(newMaximum, minimumLimit + 1, maximumLimit);
        }

        #region UpdateRate
        s.RegisterAction($"{Name}::UpdateRate::Set", b => b.WithSetting<int>(s => s.WithLabel("Update rate").WithDescription("Will be set to closest\npossible value.").WithStringFormat("{}{0} Hz"))
                                                           .WithCallback((_, updateRate) =>
                                                           {
                                                               var interval = 1000f / updateRate;
                                                               UpdateInterval = (int) UpdateIntervalTicks.OrderBy(x => Math.Abs(interval - x)).First();
                                                           }));
        #endregion

        #region AutoConnectEnabled
        s.RegisterAction($"{Name}::AutoConnectEnabled::Set", b => b.WithSetting<bool>(s => s.WithLabel("Enable auto connect")).WithCallback((_, enabled) => AutoConnectEnabled = enabled));
        s.RegisterAction($"{Name}::AutoConnectEnabled::Toggle", b => b.WithCallback(_ => AutoConnectEnabled = !AutoConnectEnabled));
        #endregion

        #region Axis::Range::Minimum
        s.RegisterAction($"{Name}::Axis::Range::Minimum::Offset", 
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, limit) => UpdateSettings(axis, s => SetMinimum(s, s.Minimum + offset, limit))));

        s.RegisterAction($"{Name}::Axis::Range::Minimum::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => SetMinimum(s, value, 0))));

        s.RegisterAction($"{Name}::Axis::Range::Minimum::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, limit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => SetMinimum(s, s.Minimum + axisGesture.Delta * 100, limit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Range::Maximum
        s.RegisterAction($"{Name}::Axis::Range::Maximum::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, limit) => UpdateSettings(axis, s => SetMaximum(s, s.Maximum + offset, limit))));

        s.RegisterAction($"{Name}::Axis::Range::Maximum::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => SetMaximum(s, value, 100))));

        s.RegisterAction($"{Name}::Axis::Range::Maximum::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, limit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => SetMaximum(s, s.Maximum + axisGesture.Delta * 100, limit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Range::Middle
        s.RegisterAction($"{Name}::Axis::Range::Middle::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetMiddle(s, offset, minimumLimit, maximumLimit))));

        s.RegisterAction($"{Name}::Axis::Range::Middle::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => OffsetMiddle(s, value - (s.Maximum - s.Minimum) / 2, 0, 100))));

        s.RegisterAction($"{Name}::Axis::Range::Middle::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, minimumLimit, maximumLimit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => OffsetMiddle(s, axisGesture.Delta * 100, minimumLimit, maximumLimit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Range::Size
        s.RegisterAction($"{Name}::Axis::Range::Size::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetSize(s, offset, minimumLimit, maximumLimit))));

        s.RegisterAction($"{Name}::Axis::Range::Size::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => OffsetSize(s, value - (s.Maximum - s.Minimum), 0, 100))));

        s.RegisterAction($"{Name}::Axis::Range::Size::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, minimumLimit, maximumLimit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => OffsetSize(s, axisGesture.Delta * 100, minimumLimit, maximumLimit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
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

    public bool UsePreciseSleep { get; set; }

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

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type)
    {
        if (type == AppSettingsMessageType.Saving)
        {
            settings[nameof(UsePreciseSleep)] = JValue.FromObject(UsePreciseSleep);
        }
        else if (type == AppSettingsMessageType.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(UsePreciseSleep), out var usePreciseSleep))
                UsePreciseSleep = usePreciseSleep;
        }
    }

    protected void Sleep(Stopwatch stopwatch)
    {
        static float ElapsedMiliseconds(Stopwatch stopwatch)
            => stopwatch.ElapsedTicks * 1000f / Stopwatch.Frequency;

        static void SleepPrecise(Stopwatch stopwatch, float desiredMs)
        {
            while (true)
            {
                var elapsed = ElapsedMiliseconds(stopwatch);
                var diff = desiredMs - elapsed;
                if (diff <= 0f)
                    break;

                if (diff < 1f) Thread.SpinWait(10);
                else if (diff < 2f) Thread.SpinWait(100);
                else if (diff < 5f) Thread.Sleep(1);
                else if (diff < 15f) Thread.Sleep(5);
                else Thread.Sleep(10);
            }
        }

        if (!UsePreciseSleep)
            Thread.Sleep((int)MathF.Max(1, UpdateInterval - ElapsedMiliseconds(stopwatch)));
        else
            SleepPrecise(stopwatch, UpdateInterval);

        UpdateStats(stopwatch);
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

    protected override void HandleSettings(JObject settings, AppSettingsMessageType type) { }

    protected async Task Sleep(Stopwatch stopwatch, CancellationToken token)
    {
        static float ElapsedMiliseconds(Stopwatch stopwatch)
            => stopwatch.ElapsedTicks * 1000f / Stopwatch.Frequency;

        await Task.Delay((int)MathF.Max(1, UpdateInterval - ElapsedMiliseconds(stopwatch)), token);
        UpdateStats(stopwatch);
    }

    protected override async void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        await DisconnectAsync();
    }
}
