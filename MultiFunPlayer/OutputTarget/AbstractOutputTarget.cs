using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;

namespace MultiFunPlayer.OutputTarget;

public abstract class AbstractOutputTarget : Screen, IOutputTarget
{
    private readonly IDeviceAxisValueProvider _valueProvider;
    private readonly AsyncManualResetEvent _statusEvent;
    private double _statsTime;
    private int _statsCount;
    private int _statsJitter = int.MinValue;

    public string Name => GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
    public string Identifier => $"{Name}/{InstanceIndex}";
    public int InstanceIndex { get; }

    [SuppressPropertyChangedWarnings] public abstract ConnectionStatus Status { get; protected set; }
    public bool ContentVisible { get; set; } = false;
    public bool AutoConnectEnabled { get; set; } = false;

    public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
    public int UpdateInterval { get; set; }
    public virtual int MinimumUpdateInterval { get; } = 3;
    public virtual int MaximumUpdateInterval { get; } = 33;
    public int AverageUpdateRate { get; private set; }
    public int UpdateRateJitter { get; private set; }
    public virtual DoubleCollection UpdateIntervalTicks
    {
        get
        {
            var ticks = new DoubleCollection();
            for (var i = MaximumUpdateInterval; i >= MinimumUpdateInterval; i--)
                ticks.Add(i);

            return ticks;
        }
    }

    protected Dictionary<DeviceAxis, double> Values { get; }
    protected IEventAggregator EventAggregator { get; }

    protected AbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    {
        InstanceIndex = instanceIndex;
        EventAggregator = eventAggregator;
        _valueProvider = valueProvider;

        _statusEvent = new AsyncManualResetEvent();
        Values = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
        AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(DeviceAxis.All.ToDictionary(a => a, _ => new DeviceAxisSettings()));
        UpdateInterval = 10;

        PropertyChanged += (s, e) =>
        {
            if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                _statusEvent.Reset();
        };
    }

    public abstract Task ConnectAsync();
    public abstract Task DisconnectAsync();

    protected abstract Task<bool> OnConnectingAsync();
    protected abstract Task OnDisconnectingAsync();

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

    protected virtual double CoerceProviderValue(DeviceAxis axis, double value)
    {
        if (!double.IsFinite(value))
            return axis.DefaultValue;

        return value;
    }

    protected void UpdateValues()
    {
        foreach (var axis in DeviceAxis.All)
        {
            var value = CoerceProviderValue(axis, _valueProvider?.GetValue(axis) ?? double.NaN);
            var settings = AxisSettings[axis];
            Values[axis] = MathUtils.Lerp(settings.Minimum / 100, settings.Maximum / 100, value);
        }
    }

    protected void UpdateStats(double elapsed)
    {
        _statsTime += elapsed;
        _statsCount++;

        var updateRateDiff = (int)Math.Round(Math.Abs(1000d / UpdateInterval - 1 / elapsed));
        _statsJitter = Math.Max(_statsJitter, updateRateDiff);

        if (_statsTime > 0.25)
        {
            UpdateRateJitter = _statsJitter;
            AverageUpdateRate = (int)Math.Round(1 / (_statsTime / _statsCount));
            _statsTime = 0;
            _statsCount = 0;
            _statsJitter = int.MinValue;
        }
    }

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            settings[nameof(UpdateInterval)] = UpdateInterval;
            settings[nameof(AutoConnectEnabled)] = AutoConnectEnabled;
            settings[nameof(AxisSettings)] = JObject.FromObject(AxisSettings);
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<int>(nameof(UpdateInterval), out var updateInterval))
                UpdateInterval = updateInterval;
            if (settings.TryGetValue<bool>(nameof(AutoConnectEnabled), out var autoConnectEnabled))
                AutoConnectEnabled = autoConnectEnabled;

            if (settings.TryGetValue<Dictionary<DeviceAxis, DeviceAxisSettings>>(nameof(AxisSettings), out var axisSettingsMap))
                foreach (var (axis, axisSettings) in axisSettingsMap)
                    AxisSettings[axis] = axisSettings;
        }
    }

    public virtual void RegisterActions(IShortcutManager s)
    {
        void UpdateSettings(DeviceAxis axis, Action<DeviceAxisSettings> callback)
        {
            if (axis != null)
                callback(AxisSettings[axis]);
        }

        static void SetMinimum(DeviceAxisSettings settings, double value, double minimumLimit) => settings.Minimum = MathUtils.Clamp(value, minimumLimit, settings.Maximum - 1);
        static void SetMaximum(DeviceAxisSettings settings, double value, double maximumLimit) => settings.Maximum = MathUtils.Clamp(value, settings.Minimum + 1, maximumLimit);

        static void OffsetMiddle(DeviceAxisSettings settings, double offset, double minimumLimit, double maximumLimit)
        {
            if (offset > 0 && settings.Maximum + offset > maximumLimit)
                offset = Math.Min(offset, maximumLimit - settings.Maximum);
            else if (offset < 0 && settings.Minimum + offset < minimumLimit)
                offset = Math.Max(offset, minimumLimit - settings.Minimum);

            settings.Minimum = MathUtils.Clamp(settings.Minimum + offset, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.Clamp(settings.Maximum + offset, minimumLimit + 1, maximumLimit);
        }

        static void OffsetSize(DeviceAxisSettings settings, double offset, double minimumLimit, double maximumLimit)
        {
            var middle = (settings.Maximum + settings.Minimum) / 2d;
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
        s.RegisterAction($"{Identifier}::UpdateRate::Set",
            b => b.WithSetting<int>(s => s.WithLabel("Update rate").WithDescription("Will be set to closest\npossible value.").WithStringFormat("{}{0} Hz"))
                  .WithCallback((_, updateRate) =>
                  {
                      var interval = 1000d / updateRate;
                      UpdateInterval = (int)UpdateIntervalTicks.OrderBy(x => Math.Abs(interval - x)).First();
                  }));
        #endregion

        #region AutoConnectEnabled
        s.RegisterAction($"{Identifier}::AutoConnectEnabled::Set", b => b.WithSetting<bool>(s => s.WithLabel("Enable auto connect")).WithCallback((_, enabled) => AutoConnectEnabled = enabled));
        s.RegisterAction($"{Identifier}::AutoConnectEnabled::Toggle", b => b.WithCallback(_ => AutoConnectEnabled = !AutoConnectEnabled));
        #endregion

        #region Axis::Range::Minimum
        s.RegisterAction($"{Identifier}::Axis::Range::Minimum::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, limit) => UpdateSettings(axis, s => SetMinimum(s, s.Minimum + offset, limit))));

        s.RegisterAction($"{Identifier}::Axis::Range::Minimum::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => SetMinimum(s, value, 0))));

        s.RegisterAction($"{Identifier}::Axis::Range::Minimum::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, limit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => SetMinimum(s, s.Minimum + axisGesture.Delta * 100, limit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Range::Maximum
        s.RegisterAction($"{Identifier}::Axis::Range::Maximum::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, limit) => UpdateSettings(axis, s => SetMaximum(s, s.Maximum + offset, limit))));

        s.RegisterAction($"{Identifier}::Axis::Range::Maximum::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => SetMaximum(s, value, 100))));

        s.RegisterAction($"{Identifier}::Axis::Range::Maximum::Drive",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"))
                  .WithCallback((gesture, axis, limit) =>
                    {
                        if (gesture is IAxisInputGesture axisGesture)
                            UpdateSettings(axis, s => SetMaximum(s, s.Maximum + axisGesture.Delta * 100, limit));
                    }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Range::Middle
        s.RegisterAction($"{Identifier}::Axis::Range::Middle::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetMiddle(s, offset, minimumLimit, maximumLimit))));

        s.RegisterAction($"{Identifier}::Axis::Range::Middle::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => OffsetMiddle(s, value - (s.Maximum - s.Minimum) / 2, 0, 100))));

        s.RegisterAction($"{Identifier}::Axis::Range::Middle::Drive",
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
        s.RegisterAction($"{Identifier}::Axis::Range::Size::Offset",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"))
                  .WithSetting<float>(s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetSize(s, offset, minimumLimit, maximumLimit))));

        s.RegisterAction($"{Identifier}::Axis::Range::Size::Set",
            b => b.WithSetting<DeviceAxis>(s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<int>(s => s.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => OffsetSize(s, value - (s.Maximum - s.Minimum), 0, 100))));

        s.RegisterAction($"{Identifier}::Axis::Range::Size::Drive",
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

    public virtual void UnregisterActions(IShortcutManager s)
    {
        s.UnregisterAction($"{Identifier}::UpdateRate::Set");
        s.UnregisterAction($"{Identifier}::AutoConnectEnabled::Set");
        s.UnregisterAction($"{Identifier}::AutoConnectEnabled::Toggle");
        s.UnregisterAction($"{Identifier}::Axis::Range::Minimum::Offset");
        s.UnregisterAction($"{Identifier}::Axis::Range::Minimum::Set");
        s.UnregisterAction($"{Identifier}::Axis::Range::Minimum::Drive");
        s.UnregisterAction($"{Identifier}::Axis::Range::Maximum::Offset");
        s.UnregisterAction($"{Identifier}::Axis::Range::Maximum::Set");
        s.UnregisterAction($"{Identifier}::Axis::Range::Maximum::Drive");
        s.UnregisterAction($"{Identifier}::Axis::Range::Middle::Offset");
        s.UnregisterAction($"{Identifier}::Axis::Range::Middle::Set");
        s.UnregisterAction($"{Identifier}::Axis::Range::Middle::Drive");
        s.UnregisterAction($"{Identifier}::Axis::Range::Size::Offset");
        s.UnregisterAction($"{Identifier}::Axis::Range::Size::Set");
        s.UnregisterAction($"{Identifier}::Axis::Range::Size::Drive");
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

    protected ThreadAbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider) { }

    protected abstract void Run(CancellationToken token);

    public override async Task ConnectAsync()
    {
        if (Status != ConnectionStatus.Disconnected)
            return;

        Status = ConnectionStatus.Connecting;
        if (!await OnConnectingAsync())
            _ = Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true));
    }

    protected override async Task<bool> OnConnectingAsync()
    {
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

        return await Task.FromResult(true);
    }

    public override async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
            return;

        Status = ConnectionStatus.Disconnecting;
        await OnDisconnectingAsync().ConfigureAwait(true);
        Status = ConnectionStatus.Disconnected;
    }

    protected override async Task OnDisconnectingAsync()
    {
        _cancellationSource?.Cancel();
        _thread?.Join();

        await Task.Delay(250);
        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _thread = null;
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(UsePreciseSleep)] = UsePreciseSleep;
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(UsePreciseSleep), out var usePreciseSleep))
                UsePreciseSleep = usePreciseSleep;
        }
    }

    protected void FixedUpdate(Func<bool> condition, Action<double> body)
    {
        static void SleepPrecise(Stopwatch stopwatch, double millisecondsTimeout)
        {
            if (millisecondsTimeout < 0)
                return;

            var frequencyInverse = 1d / Stopwatch.Frequency;
            while (true)
            {
                var elapsed = stopwatch.ElapsedTicks * frequencyInverse * 1000;
                var diff = millisecondsTimeout - elapsed;
                if (diff <= 0)
                    break;

                if (diff < 1) Thread.SpinWait(10);
                else if (diff < 2) Thread.SpinWait(100);
                else if (diff < 5) Thread.Sleep(1);
                else if (diff < 15) Thread.Sleep(5);
                else Thread.Sleep(10);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        while (condition())
        {
            var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            body(elapsed);
            UpdateStats(elapsed);

            if (!UsePreciseSleep)
                Thread.Sleep((int)Math.Max(1, UpdateInterval - stopwatch.ElapsedTicks / (double)Stopwatch.Frequency * 1000));
            else
                SleepPrecise(stopwatch, UpdateInterval);
        }
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

    protected AsyncAbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider) { }

    protected abstract Task RunAsync(CancellationToken token);

    public override async Task ConnectAsync()
    {
        if (Status != ConnectionStatus.Disconnected)
            return;

        Status = ConnectionStatus.Connecting;
        if (!await OnConnectingAsync())
            _ = Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true));
    }

    protected override async Task<bool> OnConnectingAsync()
    {
        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
        _ = _task.ContinueWith(_ => Execute.OnUIThreadAsync(async () => await DisconnectAsync().ConfigureAwait(true))).Unwrap();

        return await Task.FromResult(true);
    }

    public override async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
            return;

        Status = ConnectionStatus.Disconnecting;
        await OnDisconnectingAsync().ConfigureAwait(true);
        Status = ConnectionStatus.Disconnected;
    }

    protected override async Task OnDisconnectingAsync()
    {
        _cancellationSource?.Cancel();

        if (_task != null)
            await _task;

        await Task.Delay(250);
        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _task = null;
    }

    protected async Task FixedUpdateAsync(Func<bool> condition, Func<double, Task> body, CancellationToken token)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(UpdateInterval));
        var timerInterval = UpdateInterval;

        var stopwatch = Stopwatch.StartNew();
        while (condition())
        {
            var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            await body(elapsed);
            UpdateStats(elapsed);

            _ = await timer.WaitForNextTickAsync(token);

            if (UpdateInterval != timerInterval)
            {
                timer.Dispose();
                timer = new PeriodicTimer(TimeSpan.FromMilliseconds(UpdateInterval));
                timerInterval = UpdateInterval;
            }
        }

        timer.Dispose();
    }

    protected override async void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        await DisconnectAsync();
    }
}
