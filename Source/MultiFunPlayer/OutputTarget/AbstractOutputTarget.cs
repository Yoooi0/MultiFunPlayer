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

namespace MultiFunPlayer.OutputTarget;

internal abstract class AbstractOutputTarget : Screen, IOutputTarget
{
    private readonly IDeviceAxisValueProvider _valueProvider;

    public string Name { get; init; }
    public string Identifier => $"{Name}/{InstanceIndex}";
    public int InstanceIndex { get; }

    [SuppressPropertyChangedWarnings] public abstract ConnectionStatus Status { get; protected set; }
    public bool ContentVisible { get; set; } = false;
    public bool AutoConnectEnabled { get; set; } = false;

    public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
    public Dictionary<DeviceAxisUpdateType, IUpdateContext> UpdateContexts { get; }
    public IReadOnlyCollection<DeviceAxisUpdateType> AvailableUpdateTypes => UpdateContexts.Keys;

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
        UpdateContexts = [];

        RegisterUpdateContexts();
    }

    protected abstract void RegisterUpdateContexts();
    protected abstract IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType);

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

    public virtual void HandleSettings(JObject settings, SettingsAction action)
    {
        if (action == SettingsAction.Saving)
        {
            settings[nameof(AutoConnectEnabled)] = AutoConnectEnabled;
            settings[nameof(AxisSettings)] = JObject.FromObject(AxisSettings);

            //TODO: move to separate object per update type
            foreach (var (_, context) in UpdateContexts)
                settings.Merge(JObject.FromObject(context), new JsonMergeSettings() { MergeArrayHandling = MergeArrayHandling.Replace });
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<bool>(nameof(AutoConnectEnabled), out var autoConnectEnabled))
                AutoConnectEnabled = autoConnectEnabled;
            if (settings.TryGetValue<Dictionary<DeviceAxis, JObject>>(nameof(AxisSettings), out var axisSettingsMap))
                foreach (var (axis, axisSettingsToken) in axisSettingsMap)
                    axisSettingsToken.Populate(AxisSettings[axis]);

            //TODO: move to separate object per update type
            foreach (var (_, context) in UpdateContexts)
                settings.Populate(context);
        }
    }

    public virtual void RegisterActions(IShortcutManager s)
    {
        void UpdateSettings(DeviceAxis axis, Action<DeviceAxisSettings> callback)
        {
            if (axis != null)
                callback(AxisSettings[axis]);
        }

        static void SetMinimum(DeviceAxisSettings settings, double newMinimum, double minimumLimit) => settings.Minimum = MathUtils.ClampSafe(newMinimum, minimumLimit, settings.Maximum - 1);
        static void SetMaximum(DeviceAxisSettings settings, double newMaximum, double maximumLimit) => settings.Maximum = MathUtils.ClampSafe(newMaximum, settings.Minimum + 1, maximumLimit);

        static void SetMiddle(DeviceAxisSettings settings, IAxisInputGestureData data, double minimumLimit, double maximumLimit)
        {
            var size = (settings.Maximum - settings.Minimum) / 2;
            var newMiddle = data.ApplyTo((settings.Maximum + settings.Minimum) / 2, 100);

            if (newMiddle + size > maximumLimit)
                newMiddle = maximumLimit - size;
            else if (newMiddle - size < minimumLimit)
                newMiddle = minimumLimit + size;

            settings.Minimum = MathUtils.ClampSafe(newMiddle - size, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.ClampSafe(newMiddle + size, minimumLimit + 1, maximumLimit);
        }

        static void OffsetMiddle(DeviceAxisSettings settings, double offset, double minimumLimit, double maximumLimit)
        {
            if (offset > 0 && settings.Maximum + offset > maximumLimit)
                offset = Math.Min(offset, maximumLimit - settings.Maximum);
            else if (offset < 0 && settings.Minimum + offset < minimumLimit)
                offset = Math.Max(offset, minimumLimit - settings.Minimum);

            settings.Minimum = MathUtils.ClampSafe(settings.Minimum + offset, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.ClampSafe(settings.Maximum + offset, minimumLimit + 1, maximumLimit);
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
            var newRange = MathUtils.ClampSafe(settings.Maximum - settings.Minimum + offset, 1, maximumLimit - minimumLimit);
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

            settings.Minimum = MathUtils.ClampSafe(newMinimum, minimumLimit, maximumLimit - 1);
            settings.Maximum = MathUtils.ClampSafe(newMaximum, minimumLimit + 1, maximumLimit);
        }

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

internal abstract class ThreadAbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : AbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    protected abstract void Run(CancellationToken token);

    protected sealed override void RegisterUpdateContexts()
    {
        foreach(var updateType in Enum.GetValues<DeviceAxisUpdateType>())
        {
            var context = RegisterUpdateContext(updateType);
            if (context == null)
                continue;

            if (updateType == DeviceAxisUpdateType.FixedUpdate && context is not ThreadFixedUpdateContext)
                throw new UnreachableException();
            if (updateType == DeviceAxisUpdateType.PolledUpdate && context is not ThreadPolledUpdateContext)
                throw new UnreachableException();

            UpdateContexts[updateType] = context;
        }
    }

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

    protected void FixedUpdate(Func<bool> condition, Action<ThreadFixedUpdateContext, double> body)
        => FixedUpdate<ThreadFixedUpdateContext>(condition, body);
    protected void FixedUpdate<T>(Func<bool> condition, Action<T, double> body) where T : ThreadFixedUpdateContext
    {
        var stopwatch = Stopwatch.StartNew();
        var context = (T)UpdateContexts[DeviceAxisUpdateType.FixedUpdate];
        while (condition())
        {
            var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            body(context, elapsed);
            context.UpdateStats(elapsed);

            if (!context.UsePreciseSleep)
                Thread.Sleep((int)Math.Max(1, context.UpdateInterval - stopwatch.ElapsedTicks / (double)Stopwatch.Frequency * 1000));
            else
                stopwatch.SleepPrecise(context.UpdateInterval);
        }
    }
    protected void PolledUpdate(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Action<ThreadPolledUpdateContext, DeviceAxis, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
        => PolledUpdate<ThreadPolledUpdateContext>(axes, condition, body, cancellationToken);
    protected void PolledUpdate<T>(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Action<T, DeviceAxis, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken) where T : ThreadPolledUpdateContext
    {
        axes ??= DeviceAxis.All;

        try
        {
            BeginSnapshotPolling();

            var context = (T)UpdateContexts[DeviceAxisUpdateType.PolledUpdate];
            var stopwatches = axes.ToDictionary(a => a, _ => new Stopwatch());
            while (condition())
            {
                (var axis, var snapshot) = WaitForSnapshotAny(axes, cancellationToken);

                var stopwatch = stopwatches[axis];
                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(context, axis, snapshot, elapsed);
                context.UpdateStats(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }

    protected void PolledUpdate(DeviceAxis axis, Func<bool> condition, Action<ThreadPolledUpdateContext, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken)
        => PolledUpdate<ThreadPolledUpdateContext>(axis, condition, body, cancellationToken);
    protected void PolledUpdate<T>(DeviceAxis axis, Func<bool> condition, Action<T, DeviceAxisScriptSnapshot, double> body, CancellationToken cancellationToken) where T : ThreadPolledUpdateContext
    {
        var stopwatch = new Stopwatch();

        try
        {
            BeginSnapshotPolling();

            var context = (T)UpdateContexts[DeviceAxisUpdateType.PolledUpdate];
            while (condition())
            {
                (var success, var snapshot) = WaitForSnapshot(axis, cancellationToken);
                if (!success)
                    return;

                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                body(context, snapshot, elapsed);
                context.UpdateStats(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }
}

internal abstract class AsyncAbstractOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : AbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private CancellationTokenSource _cancellationSource;
    private Task _task;

    protected abstract Task RunAsync(CancellationToken token);

    protected sealed override void RegisterUpdateContexts()
    {
        foreach (var updateType in Enum.GetValues<DeviceAxisUpdateType>())
        {
            var context = RegisterUpdateContext(updateType);
            if (context == null)
                continue;

            if (updateType == DeviceAxisUpdateType.FixedUpdate && context is not AsyncFixedUpdateContext)
                throw new UnreachableException();
            if (updateType == DeviceAxisUpdateType.PolledUpdate && context is not AsyncPolledUpdateContext)
                throw new UnreachableException();

            UpdateContexts[updateType] = context;
        }
    }

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
        _task = Task.Run(() => RunAsync(_cancellationSource.Token));
        _ = _task.ContinueWith(_ => DisconnectAsync());

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

    protected Task FixedUpdateAsync(Func<bool> condition, Func<AsyncFixedUpdateContext, double, Task> body, CancellationToken token)
        => FixedUpdateAsync<AsyncFixedUpdateContext>(condition, body, token);
    protected async Task FixedUpdateAsync<T>(Func<bool> condition, Func<T, double, Task> body, CancellationToken token) where T : AsyncFixedUpdateContext
    {
        var context = (T)UpdateContexts[DeviceAxisUpdateType.FixedUpdate];
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(context.UpdateInterval));
        var timerInterval = context.UpdateInterval;

        var stopwatch = Stopwatch.StartNew();
        while (condition())
        {
            var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            await body(context, elapsed);
            context.UpdateStats(elapsed);

            _ = await timer.WaitForNextTickAsync(token);

            if (context.UpdateInterval != timerInterval)
            {
                timer.Dispose();
                timer = new PeriodicTimer(TimeSpan.FromMilliseconds(context.UpdateInterval));
                timerInterval = context.UpdateInterval;
            }
        }

        timer.Dispose();
    }

    protected Task PolledUpdateAsync(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Func<AsyncPolledUpdateContext, DeviceAxis, DeviceAxisScriptSnapshot, double, Task> body, CancellationToken cancellationToken)
        => PolledUpdateAsync<AsyncPolledUpdateContext>(axes, condition, body, cancellationToken);
    protected async Task PolledUpdateAsync<T>(IReadOnlyList<DeviceAxis> axes, Func<bool> condition, Func<T, DeviceAxis, DeviceAxisScriptSnapshot, double, Task> body, CancellationToken cancellationToken) where T : AsyncPolledUpdateContext
    {
        axes ??= DeviceAxis.All;

        try
        {
            BeginSnapshotPolling();

            var context = (T)UpdateContexts[DeviceAxisUpdateType.PolledUpdate];
            var stopwatches = axes.ToDictionary(a => a, _ => new Stopwatch());
            while (condition())
            {
                (var axis, var snapshot) = await WaitForSnapshotAnyAsync(axes, cancellationToken);

                var stopwatch = stopwatches[axis];
                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                await body(context, axis, snapshot, elapsed);
                context.UpdateStats(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }

    protected Task PolledUpdateAsync(DeviceAxis axis, Func<bool> condition, Func<AsyncPolledUpdateContext, DeviceAxisScriptSnapshot, double, Task> body, CancellationToken cancellationToken)
        => PolledUpdateAsync<AsyncPolledUpdateContext>(axis, condition, body, cancellationToken);
    protected async Task PolledUpdateAsync<T>(DeviceAxis axis, Func<bool> condition, Func<T, DeviceAxisScriptSnapshot, double, Task> body, CancellationToken cancellationToken) where T : AsyncPolledUpdateContext
    {
        try
        {
            BeginSnapshotPolling();

            var stopwatch = new Stopwatch();
            var context = (T)UpdateContexts[DeviceAxisUpdateType.PolledUpdate];
            while (condition())
            {
                (var success, var snapshot) = await WaitForSnapshotAsync(axis, cancellationToken);
                if (!success)
                    return;

                var elapsed = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
                stopwatch.Restart();

                await body(context, snapshot, elapsed);
                context.UpdateStats(axis, snapshot, elapsed);
            }
        }
        finally
        {
            EndSnapshotPolling();
        }
    }
}
