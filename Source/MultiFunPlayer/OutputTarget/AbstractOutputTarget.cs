using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Shortcut;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using System.Windows.Media;

namespace MultiFunPlayer.OutputTarget;

internal abstract class AbstractOutputTarget : Screen, IOutputTarget
{
    private readonly IDeviceAxisValueProvider _valueProvider;
    private double _statsTime;
    private int _statsCount;
    private int _statsJitter = int.MinValue;

    public string Name { get; init; }
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

    protected IEventAggregator EventAggregator { get; }

    protected AbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    {
        InstanceIndex = instanceIndex;
        EventAggregator = eventAggregator;
        _valueProvider = valueProvider;

        if (this is IHandle handler)
            eventAggregator.Subscribe(handler);

        Name = GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
        AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(DeviceAxis.All.ToDictionary(a => a, _ => new DeviceAxisSettings()));
        UpdateInterval = 10;
    }

    public abstract Task ConnectAsync();
    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected || Status == ConnectionStatus.Disconnecting)
            return;

        Status = ConnectionStatus.Disconnecting;
        await Task.Delay(250);
        await OnDisconnectingAsync();
        Status = ConnectionStatus.Disconnected;
    }

    protected abstract ValueTask<bool> OnConnectingAsync();
    protected abstract ValueTask OnDisconnectingAsync();

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
        var channel = Channel.CreateUnbounded<ConnectionStatus>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        PropertyChanged += OnPropertyChanged;

        while (true)
            if (statuses.Contains(Status) || statuses.Contains(await channel.Reader.ReadAsync(token)))
                break;

        PropertyChanged -= OnPropertyChanged;

        void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, "Status", StringComparison.OrdinalIgnoreCase))
                channel.Writer.TryWrite(Status);
        }
    }

    protected double GetValue(DeviceAxis axis)
    {
        var value = _valueProvider.GetValue(axis);
        if (!double.IsFinite(value))
            return axis.DefaultValue;
        return value;
    }

    protected void GetValues(Dictionary<DeviceAxis, double> values, Func<double, double> coerceValue = null)
    {
        foreach (var axis in DeviceAxis.All)
        {
            var value = GetValue(axis);
            if (coerceValue != null)
                value = coerceValue(value);

            var settings = AxisSettings[axis];
            values[axis] = MathUtils.Lerp(settings.Minimum / 100, settings.Maximum / 100, value);
        }
    }

    protected void BeginSnapshotPolling()
        => _valueProvider.BeginSnapshotPolling(Identifier);
    protected void EndSnapshotPolling()
        => _valueProvider.EndSnapshotPolling(Identifier);
    protected (DeviceAxis, DeviceAxisScriptSnapshot) WaitForSnapshotAny(IReadOnlyList<DeviceAxis> axes, CancellationToken cancellationToken)
        => _valueProvider.WaitForSnapshotAny(axes, Identifier, cancellationToken);
    protected ValueTask<(DeviceAxis, DeviceAxisScriptSnapshot)> WaitForSnapshotAnyAsync(IReadOnlyList<DeviceAxis> axes,  CancellationToken cancellationToken)
        => _valueProvider.WaitForSnapshotAnyAsync(axes, Identifier, cancellationToken);
    protected (bool, DeviceAxisScriptSnapshot) WaitForSnapshot(DeviceAxis axis, CancellationToken cancellationToken)
        => _valueProvider.WaitForSnapshot(axis, Identifier, cancellationToken);
    protected ValueTask<(bool, DeviceAxisScriptSnapshot)> WaitForSnapshotAsync(DeviceAxis axis,  CancellationToken cancellationToken)
        => _valueProvider.WaitForSnapshotAsync(axis, Identifier, cancellationToken);

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

            if (settings.TryGetValue<Dictionary<DeviceAxis, JObject>>(nameof(AxisSettings), out var axisSettingsMap))
                foreach (var (axis, axisSettingsToken) in axisSettingsMap)
                    axisSettingsToken.Populate(AxisSettings[axis]);
        }
    }

    public virtual void RegisterActions(IShortcutManager s)
    {
        void UpdateSettings(DeviceAxis axis, Action<DeviceAxisSettings> callback)
        {
            if (axis != null)
                callback(AxisSettings[axis]);
        }

        static void SetMinimum(DeviceAxisSettings settings, double value, double minimumLimit) => settings.Minimum = Math.Clamp(value, minimumLimit, settings.Maximum - 1);
        static void SetMaximum(DeviceAxisSettings settings, double value, double maximumLimit) => settings.Maximum = Math.Clamp(value, settings.Minimum + 1, maximumLimit);

        static void SetMiddle(DeviceAxisSettings settings, IAxisInputGestureData data, double minimumLimit, double maximumLimit)
        {
            var size = (settings.Maximum - settings.Minimum) / 2;
            var newMiddle = data.ApplyTo((settings.Maximum + settings.Minimum) / 2, 100);

            if (newMiddle + size > maximumLimit)
                newMiddle = maximumLimit - size;
            else if (newMiddle - size < minimumLimit)
                newMiddle = minimumLimit + size;

            settings.Minimum = Math.Clamp(newMiddle - size, minimumLimit, maximumLimit - 1);
            settings.Maximum = Math.Clamp(newMiddle + size, minimumLimit + 1, maximumLimit);
        }

        static void OffsetMiddle(DeviceAxisSettings settings, double offset, double minimumLimit, double maximumLimit)
        {
            if (offset > 0 && settings.Maximum + offset > maximumLimit)
                offset = Math.Min(offset, maximumLimit - settings.Maximum);
            else if (offset < 0 && settings.Minimum + offset < minimumLimit)
                offset = Math.Max(offset, minimumLimit - settings.Minimum);

            settings.Minimum = Math.Clamp(settings.Minimum + offset, minimumLimit, maximumLimit - 1);
            settings.Maximum = Math.Clamp(settings.Maximum + offset, minimumLimit + 1, maximumLimit);
        }

        static void SetSize(DeviceAxisSettings settings, IAxisInputGestureData data, double minimumLimit, double maximumLimit)
        {
            var middle = (settings.Maximum + settings.Minimum) / 2;
            var newRange = data.ApplyTo(settings.Maximum - settings.Minimum, 100);
            SetMiddleAndRange(settings, middle, newRange, minimumLimit, maximumLimit);
        }

        static void OffsetSize(DeviceAxisSettings settings, double offset, double minimumLimit, double maximumLimit)
        {
            var middle = (settings.Maximum + settings.Minimum) / 2d;
            var newRange = Math.Clamp(settings.Maximum - settings.Minimum + offset, 1, maximumLimit - minimumLimit);
            SetMiddleAndRange(settings, middle, newRange, minimumLimit, maximumLimit);
        }

        static void SetMiddleAndRange(DeviceAxisSettings settings, double middle, double newRange, double minimumLimit, double maximumLimit)
        {
            var newMaximum = middle + newRange / 2;
            var newMinimum = middle - newRange / 2;

            if (newMaximum > maximumLimit)
                newMinimum -= newMaximum - maximumLimit;
            if (newMinimum < minimumLimit)
                newMaximum += minimumLimit - newMinimum;

            settings.Minimum = Math.Clamp(newMinimum, minimumLimit, maximumLimit - 1);
            settings.Maximum = Math.Clamp(newMaximum, minimumLimit + 1, maximumLimit);
        }

        #region UpdateRate
        s.RegisterAction<int>($"{Identifier}::UpdateRate::Set", s => s.WithLabel("Update rate").WithDescription("Will be set to closest\npossible value.").WithStringFormat("{}{0} Hz"), updateRate =>
        {
            var interval = 1000d / updateRate;
            UpdateInterval = (int)UpdateIntervalTicks.OrderBy(x => Math.Abs(interval - x)).First();
        });
        #endregion

        #region AutoConnectEnabled
        s.RegisterAction<bool>($"{Identifier}::AutoConnectEnabled::Set", s => s.WithLabel("Enable auto connect"), enabled => AutoConnectEnabled = enabled);
        s.RegisterAction($"{Identifier}::AutoConnectEnabled::Toggle", () => AutoConnectEnabled = !AutoConnectEnabled);
        #endregion

        #region Axis::Range::Minimum
        s.RegisterAction<DeviceAxis, int, double>($"{Identifier}::Axis::Range::Minimum::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"),
            (axis, offset, limit) => UpdateSettings(axis, s => SetMinimum(s, s.Minimum + offset, limit)));

        s.RegisterAction<DeviceAxis, int>($"{Identifier}::Axis::Range::Minimum::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => SetMinimum(s, value, 0)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis, double>($"{Identifier}::Axis::Range::Minimum::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(0).WithLabel("Value limit").WithStringFormat("{}{0}%"),
            (data, axis, limit) => UpdateSettings(axis, s => SetMinimum(s, data.ApplyTo(s.Minimum, 100), limit)));
        #endregion

        #region Axis::Range::Maximum
        s.RegisterAction<DeviceAxis, int, double>($"{Identifier}::Axis::Range::Maximum::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"),
            (axis, offset, limit) => UpdateSettings(axis, s => SetMaximum(s, s.Maximum + offset, limit)));

        s.RegisterAction<DeviceAxis, int>($"{Identifier}::Axis::Range::Maximum::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => SetMaximum(s, value, 100)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis, double>($"{Identifier}::Axis::Range::Maximum::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(100).WithLabel("Value limit").WithStringFormat("{}{0}%"),
            (gesture, axis, limit) => UpdateSettings(axis, s => SetMaximum(s, gesture.ApplyTo(s.Maximum, 100), limit)));
        #endregion

        #region Axis::Range::Middle
        s.RegisterAction<DeviceAxis, int, double, double>($"{Identifier}::Axis::Range::Middle::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"),
            (axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetMiddle(s, offset, minimumLimit, maximumLimit)));

        s.RegisterAction<DeviceAxis, int>($"{Identifier}::Axis::Range::Middle::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => OffsetMiddle(s, value - (s.Maximum - s.Minimum) / 2, 0, 100)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis, double, double>($"{Identifier}::Axis::Range::Middle::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"),
            (data, axis, minimumLimit, maximumLimit) => UpdateSettings(axis, s => SetMiddle(s, data, minimumLimit, maximumLimit)));
        #endregion

        #region Axis::Range::Size
        s.RegisterAction<DeviceAxis, int, double, double>($"{Identifier}::Axis::Range::Size::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"),
            (axis, offset, minimumLimit, maximumLimit) => UpdateSettings(axis, s => OffsetSize(s, offset, minimumLimit, maximumLimit)));

        s.RegisterAction<DeviceAxis, int>($"{Identifier}::Axis::Range::Size::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => OffsetSize(s, value - (s.Maximum - s.Minimum), 0, 100)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis, double, double>($"{Identifier}::Axis::Range::Size::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue(0).WithLabel("Minimum limit").WithStringFormat("{}{0}%"),
            s => s.WithDefaultValue(100).WithLabel("Maximium limit").WithStringFormat("{}{0}%"),
            (data, axis, minimumLimit, maximumLimit) => UpdateSettings(axis, s => SetSize(s, data, minimumLimit, maximumLimit)));
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

    protected virtual void Dispose(bool disposing)
    {
        var valueTask = OnDisconnectingAsync();
        if (!valueTask.IsCompleted)
            valueTask.AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal abstract class ThreadAbstractOutputTarget : AbstractOutputTarget
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
            await DisconnectAsync();
    }

    protected override ValueTask<bool> OnConnectingAsync()
    {
        _cancellationSource = new CancellationTokenSource();
        _thread = new Thread(() =>
        {
            Run(_cancellationSource.Token);
            _ = DisconnectAsync();
        })
        {
            IsBackground = true
        };
        _thread.Start();

        return ValueTask.FromResult(true);
    }

    private int _isDisconnectingFlag;
    protected override ValueTask OnDisconnectingAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisconnectingFlag, 1, 0) != 0)
            return ValueTask.CompletedTask;

        _cancellationSource?.Cancel();
        _thread?.Join();

        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _thread = null;

        Interlocked.Decrement(ref _isDisconnectingFlag);
        return ValueTask.CompletedTask;
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
                stopwatch.SleepPrecise(UpdateInterval);
        }
    }

    protected void PolledUpdate(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Action<DeviceAxis, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
    {
        axes ??= DeviceAxis.All;

        try
        {
            BeginSnapshotPolling();
            var stopwatches = axes.ToDictionary(a => a, _ => new Stopwatch());
            while (condition())
            {
                (var axis, var snapshot) = WaitForSnapshotAny(axes, cancellationToken);

                var stopwatch = stopwatches[axis];
                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }

    protected void PolledUpdate(DeviceAxis axis, Func<bool> condition, Action<DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();

        try
        {
            BeginSnapshotPolling();
            while (condition())
            {
                (var success, var snapshot) = WaitForSnapshot(axis, cancellationToken);
                if (!success)
                    return;

                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }
}

internal abstract class AsyncAbstractOutputTarget : AbstractOutputTarget
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
            await DisconnectAsync();
    }

    protected override ValueTask<bool> OnConnectingAsync()
    {
        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
        _ = _task.ContinueWith(_ => DisconnectAsync()).Unwrap();

        return ValueTask.FromResult(true);
    }

    private int _isDisconnectingFlag;
    protected override async ValueTask OnDisconnectingAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisconnectingFlag, 1, 0) != 0)
            return;

        _cancellationSource?.Cancel();
        if (_task != null)
            await _task;

        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _task = null;

        Interlocked.Decrement(ref _isDisconnectingFlag);
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

    protected async Task PolledUpdateAsync(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Action<DeviceAxis, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
    {
        axes ??= DeviceAxis.All;

        try
        {
            BeginSnapshotPolling();
            var stopwatches = axes.ToDictionary(a => a, _ => new Stopwatch());
            while (condition())
            {
                (var axis, var snapshot) = await WaitForSnapshotAnyAsync(axes, cancellationToken);

                var stopwatch = stopwatches[axis];
                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }

    protected async Task PolledUpdateAsync(DeviceAxis axis, Func<bool> condition, Action<DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();
        try
        {
            BeginSnapshotPolling();
            while (condition())
            {
                (var success, var snapshot) = await WaitForSnapshotAsync(axis, cancellationToken);
                if (!success)
                    return;

                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }
}
