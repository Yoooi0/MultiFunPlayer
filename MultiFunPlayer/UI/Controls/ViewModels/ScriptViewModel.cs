using MultiFunPlayer.Common;
using Stylet;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.IO.Compression;
using PropertyChanged;
using Newtonsoft.Json.Linq;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.ComponentModel;
using NLog;
using System.Runtime.CompilerServices;
using MultiFunPlayer.Input;
using MultiFunPlayer.MotionProvider;
using MaterialDesignThemes.Wpf;
using System.Reflection;
using MultiFunPlayer.MediaSource.MediaResource;
using MultiFunPlayer.MediaSource.MediaResource.Modifier;
using MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels;

namespace MultiFunPlayer.UI.Controls.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ScriptViewModel : Screen, IDeviceAxisValueProvider, IDisposable,
    IHandle<MediaPositionChangedMessage>, IHandle<MediaPlayingChangedMessage>, IHandle<MediaPathChangedMessage>, IHandle<MediaDurationChangedMessage>,
    IHandle<MediaSpeedChangedMessage>, IHandle<AppSettingsMessage>, IHandle<SyncRequestMessage>, IHandle<ScriptLoadMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly IMediaResourceFactory _mediaResourceFactory;
    private Thread _updateThread;
    private CancellationTokenSource _cancellationSource;
    private double _internalMediaPosition;

    public bool IsPlaying { get; private set; }
    public double PlaybackSpeed { get; private set; }
    public double MediaDuration { get; private set; }

    [DoNotNotify]
    public double MediaPosition { get; private set; }

    public ObservableConcurrentDictionary<DeviceAxis, AxisModel> AxisModels { get; set; }
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisState> AxisStates { get; }
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, KeyframeCollection> AxisKeyframes { get; }

    public Dictionary<string, Type> MediaPathModifierTypes { get; }
    public IMotionProviderManager MotionProviderManager { get; }

    public MediaResourceInfo MediaResource { get; set; }

    [JsonProperty] public double GlobalOffset { get; set; }
    [JsonProperty] public bool ValuesContentVisible { get; set; }
    [JsonProperty] public bool MediaContentVisible { get; set; } = true;
    [JsonProperty] public bool AxisContentVisible { get; set; } = false;
    [JsonProperty] public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisSettings> AxisSettings { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public ObservableConcurrentCollection<IMediaPathModifier> MediaPathModifiers => _mediaResourceFactory.PathModifiers;
    [JsonProperty] public ObservableConcurrentCollection<ScriptLibrary> ScriptLibraries { get; }
    [JsonProperty] public SyncSettings SyncSettings { get; set; }
    [JsonProperty] public bool HeatmapShowStrokeLength { get; set; }
    [JsonProperty] public int HeatmapBucketCount { get; set; } = 333;
    [JsonProperty] public bool HeatmapInvertY { get; set; } = false;
    [JsonProperty] public bool AutoSkipToScriptStartEnabled { get; set; } = true;
    [JsonProperty] public double AutoSkipToScriptStartOffset { get; set; } = 5;

    public bool IsSyncing => AxisStates.Values.Any(s => s.SyncTime > 0);
    public double SyncProgress => !IsSyncing ? 100 : GetSyncProgress(AxisStates.Values.Max(s => s.SyncTime), SyncSettings.Duration) * 100;

    public ScriptViewModel(IShortcutManager shortcutManager, IMotionProviderManager motionProviderManager, IMediaResourceFactory mediaResourceFactory, IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);

        MotionProviderManager = motionProviderManager;
        _mediaResourceFactory = mediaResourceFactory;

        AxisModels = new ObservableConcurrentDictionary<DeviceAxis, AxisModel>(DeviceAxis.All.ToDictionary(a => a, _ => new AxisModel()));
        MediaPathModifierTypes = ReflectionUtils.FindImplementations<IMediaPathModifier>()
                                                .ToDictionary(t => t.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName, t => t);

        ScriptLibraries = new ObservableConcurrentCollection<ScriptLibrary>();
        SyncSettings = new SyncSettings();

        InvalidateMediaState();

        IsPlaying = false;

        AxisStates = AxisModels.CreateView(model => model.State);
        AxisSettings = AxisModels.CreateView(model => model.Settings);
        AxisKeyframes = AxisModels.CreateView(model => model.Script?.Keyframes, "Script");

        foreach (var (_, settings) in AxisSettings)
            settings.PropertyChanged += OnAxisSettingsPropertyChanged;

        _cancellationSource = new CancellationTokenSource();
        _updateThread = new Thread(() => UpdateThread(_cancellationSource.Token)) { IsBackground = true };
        _updateThread.Start();

        ResetSync(false);
        RegisterShortcuts(shortcutManager);
    }

    private ref struct AxisUpdateContext
    {
        private readonly AxisState _state;

        public double LastValue => _state.Value;
        public double Value { get; set; } = double.NaN;
        public bool IsDirty { get; set; } = false;
        public bool InsideGap { get; set; } = false;

        public AxisUpdateContext(AxisState state)
        {
            _state = state;
        }

        public void Commit()
        {
            if (!double.IsFinite(LastValue) && double.IsFinite(Value))
                IsDirty = true;

            _state.InsideGap = InsideGap;
            _state.IsDirty = IsDirty;
            if (IsDirty)
                _state.Value = Value;
        }
    }

    private void UpdateThread(CancellationToken token)
    {
        const double uiUpdateInterval = 1d / 60d;
        var uiUpdateTime = 0d;
        var deltaTime = 0d;

        while (!token.IsCancellationRequested)
        {
            var updateStartTicks = Stopwatch.GetTimestamp();

            var dirty = UpdateValues();
            UpdateUi();

            Thread.Sleep(IsPlaying || dirty ? 2 : 10);
            deltaTime = (Stopwatch.GetTimestamp() - updateStartTicks) / (double)Stopwatch.Frequency;
        }

        bool UpdateValues()
        {
            if (IsPlaying)
            {
                _internalMediaPosition += deltaTime * PlaybackSpeed;

                var error = _internalMediaPosition - MediaPosition;
                MediaPosition += MathUtils.Clamp(error, deltaTime * PlaybackSpeed * 0.9, deltaTime * PlaybackSpeed * 1.1);
            }

            var dirty = false;

            foreach (var axis in DeviceAxis.All)
                Monitor.Enter(AxisStates[axis]);

            foreach (var axis in DeviceAxis.All)
            {
                var state = AxisStates[axis];
                var settings = AxisSettings[axis];

                var context = new AxisUpdateContext(state);

                if (!settings.Bypass)
                {
                    context.IsDirty |= UpdateScript(axis, state, settings, ref context);
                    context.IsDirty |= UpdateMotionProvider(axis, state, settings, ref context);

                    if (!context.IsDirty && double.IsFinite(state.OverrideValue))
                    {
                        context.IsDirty = Math.Abs(context.LastValue - state.OverrideValue) > 0.000001;
                        context.Value = state.OverrideValue;
                        state.OverrideValue = double.NaN;
                    }
                }

                context.IsDirty |= UpdateSync(axis, state, ref context);
                context.IsDirty |= UpdateAutoHome(axis, state, settings, ref context);
                context.IsDirty |= UpdateSmartLimit(axis, state, settings, ref context);

                SpeedLimit(axis, state, settings, ref context);

                context.Commit();
                dirty |= context.IsDirty;
            }

            foreach (var axis in DeviceAxis.All)
                Monitor.Exit(AxisStates[axis]);

            return dirty;

            void SpeedLimit(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                static bool SpeedLimitInternal(AxisSettings settings, double deltaTime, ref AxisUpdateContext context)
                {
                    if (!settings.SpeedLimitEnabled)
                        return false;

                    var step = context.Value - context.LastValue;
                    if (!double.IsFinite(step))
                        return false;
                    if (Math.Abs(step) < 0.000001)
                        return false;

                    var speed = step / deltaTime;
                    var maxSpeed = 1 / settings.MaximumSecondsPerStroke;
                    if (Math.Abs(speed / maxSpeed) < 1)
                        return false;
                    if (!double.IsFinite(maxSpeed))
                        return false;

                    context.Value = MathUtils.Clamp01(context.LastValue + maxSpeed * deltaTime * Math.Sign(speed));
                    return true;
                }

                state.IsSpeedLimited = SpeedLimitInternal(settings, deltaTime, ref context);
            }

            bool UpdateSmartLimit(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                bool NoUpdate()
                {
                    state.IsSmartLimited = false;
                    return false;
                }

                if (settings.SmartLimitInputAxis == null)
                    return NoUpdate();
                if (!double.IsFinite(context.Value))
                    return NoUpdate();
                if (settings.SmartLimitPoints == null || settings.SmartLimitPoints.Count == 0)
                    return NoUpdate();

                var x = AxisStates[settings.SmartLimitInputAxis].Value * 100;
                if (!double.IsFinite(x))
                    return NoUpdate();

                var factor = Interpolation.Linear(settings.SmartLimitPoints, p => p.X, p => p.Y, x) / 100;
                state.IsSmartLimited = factor < 1;

                context.Value = settings.SmartLimitMode switch
                {
                    SmartLimitMode.Value => MathUtils.Clamp01(MathUtils.Lerp(settings.SmartLimitTargetValue, context.Value, factor)),
                    SmartLimitMode.Speed when double.IsFinite(context.LastValue) => MathUtils.Clamp01(MathUtils.Lerp(context.LastValue, context.Value, Math.Pow(factor, 4))),
                    _ => context.Value
                };

                return Math.Abs(context.LastValue - context.Value) > 0.000001;
            }

            bool UpdateScript(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                static bool NoUpdate(ref AxisUpdateContext context)
                {
                    context.InsideGap = false;
                    return false;
                }

                if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                    return NoUpdate(ref context);

                var axisPosition = GetAxisPosition(axis);
                var shouldSearch = state.Invalid
                               || (keyframes.ValidateIndex(state.Index) && keyframes[state.Index].Position > axisPosition)
                               || (state.AfterScript && keyframes[^1].Position > axisPosition);

                if (shouldSearch)
                {
                    Logger.Debug("Searching for valid index [Axis: {0}]", axis);
                    state.Index = keyframes.BinarySearch(axisPosition);
                }

                if (!IsPlaying || state.AfterScript)
                    return NoUpdate(ref context);

                var beforeIndex = state.Index;
                state.Index = keyframes.AdvanceIndex(state.Index, axisPosition);

                if (beforeIndex == -1 && state.Index >= 0)
                {
                    Logger.Debug("Resetting sync on script start [Axis: {0}]", axis);
                    ResetSyncNoLock(state);
                }

                if (!keyframes.ValidateIndex(state.Index) || !keyframes.ValidateIndex(state.Index + 1))
                {
                    if (state.Index + 1 >= keyframes.Count)
                    {
                        Logger.Debug("Resetting sync on script end [Axis: {0}]", axis);
                        state.Invalidate(end: true);
                        ResetSyncNoLock(state);
                    }

                    return NoUpdate(ref context);
                }

                context.InsideGap = keyframes.IsGap(state.Index);
                var scriptValue = MathUtils.Clamp01(keyframes.Interpolate(state.Index, axisPosition, settings.InterpolationType));
                if (settings.Inverted)
                    scriptValue = 1 - scriptValue;

                context.Value = MathUtils.Clamp01(axis.DefaultValue + (scriptValue - axis.DefaultValue) * settings.Scale / 100);
                return Math.Abs(context.LastValue - context.Value) > 0.000001;
            }

            bool UpdateMotionProvider(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                if (settings.SelectedMotionProvider == null)
                    return false;

                var providerValue = double.NaN;
                if (!settings.MotionProviderFillGaps)
                {
                    bool ShouldUpdateMotionProvider()
                    {
                        if (!settings.UpdateMotionProviderWhenPaused && !IsPlaying)
                            return false;
                        if (!settings.UpdateMotionProviderWithoutScript && !state.InsideScript)
                            return false;

                        if (settings.UpdateMotionProviderWithAxis != null)
                        {
                            var targetState = AxisStates[settings.UpdateMotionProviderWithAxis];
                            if (!targetState.IsDirty || targetState.IsAutoHoming)
                                return false;
                        }

                        return true;
                    }

                    if (ShouldUpdateMotionProvider())
                        MotionProviderManager.Update(axis, settings.SelectedMotionProvider, deltaTime);

                    providerValue = MotionProviderManager.GetValue(axis);
                    if (!double.IsFinite(providerValue))
                        return false;

                    var blendT = IsPlaying && state.InsideScript ? MathUtils.Clamp01(settings.MotionProviderBlend / 100) : 1;
                    var blendFrom = double.IsFinite(context.Value) ? context.Value : axis.DefaultValue;
                    providerValue = MathUtils.Clamp01(MathUtils.Lerp(blendFrom, providerValue, blendT));
                }
                else
                {
                    bool CanMotionProviderFillGap(ref AxisUpdateContext context)
                    {
                        if (!IsPlaying || !state.InsideScript)
                            return false;

                        if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                            return false;

                        var index = state.Index;

                        var gapStarted = !state.InsideGap && context.InsideGap;
                        var gapEnded = state.InsideGap && !context.InsideGap;
                        if (gapStarted || gapEnded)
                            ResetSyncNoLock(state);

                        return context.InsideGap && keyframes.SegmentDuration(index) >= settings.MotionProviderMinimumGapDuration;
                    }

                    if (!CanMotionProviderFillGap(ref context))
                        return false;

                    MotionProviderManager.Update(axis, settings.SelectedMotionProvider, deltaTime);
                    providerValue = MotionProviderManager.GetValue(axis);
                }

                if (!double.IsFinite(providerValue))
                    return false;

                context.Value = providerValue;
                return Math.Abs(context.LastValue - context.Value) > 0.000001;
            }

            bool UpdateAutoHome(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                bool UpdateAutoHomeInternal(ref AxisUpdateContext context)
                {
                    if (context.IsDirty || (state.InsideScript && IsPlaying))
                    {
                        state.AutoHomeTime = 0;
                        return false;
                    }

                    if (!double.IsFinite(state.Value))
                        return false;

                    if (!settings.AutoHomeEnabled)
                        return false;

                    if (settings.AutoHomeDuration < 0.0001)
                    {
                        context.Value = axis.DefaultValue;
                        return context.Value != context.LastValue;
                    }

                    state.AutoHomeTime += deltaTime;
                    var t = (state.AutoHomeTime - settings.AutoHomeDelay) / settings.AutoHomeDuration;
                    if (t < 0 || t > 1)
                        return false;

                    context.Value = MathUtils.Clamp01(MathUtils.Lerp(state.Value, axis.DefaultValue, Math.Pow(2, 10 * (t - 1))));
                    return Math.Abs(context.LastValue - context.Value) > 0.000001;
                }

                return state.IsAutoHoming = UpdateAutoHomeInternal(ref context);
            }

            bool UpdateSync(DeviceAxis axis, AxisState state, ref AxisUpdateContext context)
            {
                if (state.SyncTime <= 0)
                    return false;

                var t = GetSyncProgress(state.SyncTime, SyncSettings.Duration);
                state.SyncTime -= deltaTime;

                if (!double.IsFinite(context.Value))
                    return false;

                var from = !double.IsFinite(context.LastValue) ? axis.DefaultValue : context.LastValue;
                context.Value = MathUtils.Clamp01(MathUtils.Lerp(from, context.Value, t));

                return Math.Abs(context.LastValue - context.Value) > 0.000001;
            }
        }

        void UpdateUi()
        {
            uiUpdateTime += deltaTime;
            if (uiUpdateTime < uiUpdateInterval)
                return;

            uiUpdateTime = 0;
            Execute.OnUIThread(() =>
            {
                if (ValuesContentVisible)
                    foreach (var axis in DeviceAxis.All)
                        AxisStates[axis].NotifyValueChanged();

                NotifyOfPropertyChange(nameof(IsSyncing));
                NotifyOfPropertyChange(nameof(SyncProgress));
                NotifyOfPropertyChange(nameof(MediaPosition));
            });
        }
    }

    #region Events
    public void Handle(MediaPathChangedMessage message)
    {
        var resource = _mediaResourceFactory.CreateFromPath(message.Path);
        if (MediaResource == null && resource == null)
            return;
        if (MediaResource != null && resource != null)
            if (string.Equals(MediaResource.Name, resource.Name, StringComparison.OrdinalIgnoreCase)
             && string.Equals(MediaResource.Source, resource.Source, StringComparison.OrdinalIgnoreCase))
                return;

        Logger.Info("Received {0} [Source: \"{1}\" Name: \"{2}\"]", nameof(MediaPathChangedMessage), resource?.Source, resource?.Name);

        MediaResource = resource;
        if (SyncSettings.SyncOnMediaResourceChanged)
            ResetSync(isSyncing: MediaResource != null);

        ResetAxes(null);
        ReloadAxes(null);

        if (MediaResource == null)
            InvalidateMediaState();

        InvalidateAxisState(null);
    }

    public void Handle(MediaPlayingChangedMessage message)
    {
        if (IsPlaying == message.IsPlaying)
            return;

        Logger.Info("Received {0} [IsPlaying: {1}]", nameof(MediaPlayingChangedMessage), message.IsPlaying);

        if (SyncSettings.SyncOnMediaPlayPause)
            ResetSync();

        IsPlaying = message.IsPlaying;
    }

    public void Handle(MediaDurationChangedMessage message)
    {
        var newDuration = message.Duration?.TotalSeconds ?? double.NaN;
        if (MediaDuration == newDuration)
            return;

        Logger.Info("Received {0} [Duration: {1}]", nameof(MediaDurationChangedMessage), message.Duration?.ToString());

        MediaDuration = newDuration;
        ScheduleAutoSkipToScriptStart();
    }

    public void Handle(MediaSpeedChangedMessage message)
    {
        if (PlaybackSpeed == message.Speed)
            return;

        Logger.Info("Received {0} [Speed: {1}]", nameof(MediaSpeedChangedMessage), message.Speed);
        PlaybackSpeed = message.Speed;
    }

    public void Handle(MediaPositionChangedMessage message)
    {
        void UpdateCurrentPosition(double newPosition)
        {
            foreach (var axis in DeviceAxis.All)
                Monitor.Enter(AxisStates[axis]);

            SetMediaPositionInternal(newPosition);

            foreach (var axis in DeviceAxis.All)
                Monitor.Exit(AxisStates[axis]);
        }

        var newPosition = message.Position?.TotalSeconds ?? double.NaN;
        Logger.Trace("Received {0} [Position: {1}]", nameof(MediaPositionChangedMessage), message.Position?.ToString());

        if (!double.IsFinite(newPosition))
        {
            SetMediaPositionInternal(double.NaN);
            return;
        }

        if (!double.IsFinite(MediaPosition))
        {
            ResetSync();
            UpdateCurrentPosition(newPosition);
            return;
        }

        var error = double.IsFinite(_internalMediaPosition) ? newPosition - _internalMediaPosition : 0;
        var wasSeek = Math.Abs(error) > 1.0;
        if (wasSeek)
        {
            Logger.Debug("Detected seek: {0}", error);
            if (SyncSettings.SyncOnSeek)
                ResetSync();

            UpdateCurrentPosition(newPosition);
        }
        else
        {
            _internalMediaPosition += 0.33 * (newPosition - _internalMediaPosition);
        }
    }

    public void Handle(AppSettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            message.Settings["Script"] = JObject.FromObject(this);
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Script"))
                return;

            if (settings.TryGetValue(nameof(AxisSettings), out var axisSettingsToken))
            {
                foreach (var property in axisSettingsToken.Children<JProperty>())
                {
                    if (!DeviceAxis.TryParse(property.Name, out var axis))
                        continue;

                    property.Value.Populate(AxisSettings[axis]);
                }
            }

            if (settings.TryGetValue<List<IMediaPathModifier>>(nameof(MediaPathModifiers), new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects }, out var mediaPathModifiers))
            {
                MediaPathModifiers.Clear();
                MediaPathModifiers.AddRange(mediaPathModifiers);
            }

            if (settings.TryGetValue<List<ScriptLibrary>>(nameof(ScriptLibraries), out var scriptDirectories))
            {
                ScriptLibraries.Clear();
                ScriptLibraries.AddRange(scriptDirectories);
            }

            if (settings.TryGetValue<double>(nameof(GlobalOffset), out var globalOffset)) GlobalOffset = globalOffset;
            if (settings.TryGetValue<bool>(nameof(ValuesContentVisible), out var valuesContentVisible)) ValuesContentVisible = valuesContentVisible;
            if (settings.TryGetValue<bool>(nameof(MediaContentVisible), out var mediaContentVisible)) MediaContentVisible = mediaContentVisible;
            if (settings.TryGetValue<bool>(nameof(AxisContentVisible), out var axisContentVisible)) AxisContentVisible = axisContentVisible;
            if (settings.TryGetValue<int>(nameof(HeatmapBucketCount), out var heatmapBucketCount)) HeatmapBucketCount = heatmapBucketCount;
            if (settings.TryGetValue<bool>(nameof(HeatmapInvertY), out var heatmapInvertY)) HeatmapInvertY = heatmapInvertY;
            if (settings.TryGetValue<bool>(nameof(HeatmapShowStrokeLength), out var heatmapShowStrokeLength)) HeatmapShowStrokeLength = heatmapShowStrokeLength;
            if (settings.TryGetValue<bool>(nameof(AutoSkipToScriptStartEnabled), out var autoSkipToScriptStartEnabled)) AutoSkipToScriptStartEnabled = autoSkipToScriptStartEnabled;
            if (settings.TryGetValue<double>(nameof(AutoSkipToScriptStartOffset), out var autoSkipToScriptStartOffset)) AutoSkipToScriptStartOffset = autoSkipToScriptStartOffset;

            if (settings.TryGetValue(nameof(SyncSettings), out var syncSettingsToken)) syncSettingsToken.Populate(SyncSettings);
        }
    }

    public void Handle(SyncRequestMessage message) => ResetSync(true, message.Axes);

    public void Handle(ScriptLoadMessage message)
    {
        if (message.Scripts == null)
            return;

        Logger.Info("Received ScriptLoadMessage [Axes: {list}]", message.Scripts.Keys);
        ResetSync(true, message.Scripts.Keys);

        foreach (var (axis, script) in message.Scripts)
            SetScript(axis, script);
    }
    #endregion

    #region Common

    private void InvalidateAxisState(params DeviceAxis[] axes) => InvalidateAxisState(axes?.AsEnumerable());
    private void InvalidateAxisState(IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        Logger.Debug("Invalidating axes [Axes: {list}]", axes);
        foreach (var axis in axes)
        {
            var state = AxisStates[axis];
            lock (state)
                state.Invalidate();
        }
    }

    private double GetAxisPosition(DeviceAxis axis) => MediaPosition - GlobalOffset - AxisSettings[axis].Offset;
    public double GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetSyncProgress(double time, double duration) => MathUtils.Clamp01(Math.Pow(2, -10 * MathUtils.Clamp01(time / duration)));

    private void ResetSync(bool isSyncing = true, params DeviceAxis[] axes) => ResetSync(isSyncing, axes?.AsEnumerable());
    private void ResetSync(bool isSyncing = true, IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        Logger.Debug("Resetting sync [Axes: {list}]", axes);

        foreach (var axis in axes)
        {
            var state = AxisStates[axis];
            lock (state)
                ResetSyncNoLock(state, isSyncing);
        }

        NotifyOfPropertyChange(nameof(IsSyncing));
        NotifyOfPropertyChange(nameof(SyncProgress));
    }

    private void ResetSyncNoLock(AxisState state, bool isSyncing = true) => state.SyncTime = isSyncing ? SyncSettings.Duration : 0;
    #endregion

    #region UI Common
    [SuppressPropertyChangedWarnings]
    public void OnOffsetValueChanged(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (element.DataContext is KeyValuePair<DeviceAxis, AxisModel> pair)
        {
            var (axis, _) = pair;
            ResetSync(true, axis);
        }
        else
        {
            ResetSync();
        }
    }
    #endregion

    #region Script
    private List<DeviceAxis> SearchForScripts(IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;

        var updated = new List<DeviceAxis>();
        if (!axes.Any() || MediaResource == null)
            return updated;

        Logger.Debug("Maching files to axes [Axes: {list}]", axes);
        bool TryMatchFile(string fileName, Func<IScriptResource> generator)
        {
            var mediaWithoutExtension = Path.GetFileNameWithoutExtension(MediaResource.Name);
            var funscriptWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            if (DeviceAxis.TryParse("L0", out var strokeAxis))
            {
                if (axes.Contains(strokeAxis))
                {
                    if (string.Equals(funscriptWithoutExtension, mediaWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        SetScript(strokeAxis, generator());
                        updated.Add(strokeAxis);

                        Logger.Debug("Matched {0} script to \"{1}\"", strokeAxis.Name, fileName);
                        return true;
                    }
                }
            }

            foreach (var axis in axes)
            {
                if (axis.FunscriptNames.Any(n => funscriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
                {
                    SetScript(axis, generator());
                    updated.Add(axis);

                    Logger.Debug("Matched {0} script to \"{1}\"", axis, fileName);
                    return true;
                }
            }

            return false;
        }

        bool TryMatchArchive(string path)
        {
            if (File.Exists(path))
            {
                Logger.Info("Matching zip file \"{0}\"", path);
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(e => string.Equals(Path.GetExtension(e.FullName), ".funscript", StringComparison.OrdinalIgnoreCase)))
                    TryMatchFile(entry.Name, () => ScriptResource.FromZipArchiveEntry(path, entry));

                return true;
            }

            return false;
        }

        var mediaWithoutExtension = Path.GetFileNameWithoutExtension(MediaResource.Name);
        foreach (var library in ScriptLibraries)
        {
            Logger.Info("Searching library \"{0}\"", library.Directory);
            foreach (var zipFile in library.EnumerateFiles($"{mediaWithoutExtension}.zip"))
                TryMatchArchive(zipFile.FullName);

            foreach (var funscriptFile in library.EnumerateFiles($"{mediaWithoutExtension}*.funscript"))
                TryMatchFile(funscriptFile.Name, () => ScriptResource.FromFileInfo(funscriptFile));
        }

        if (Directory.Exists(MediaResource.Source))
        {
            Logger.Info("Searching media location \"{0}\"", MediaResource.Source);
            var sourceDirectory = new DirectoryInfo(MediaResource.Source);
            TryMatchArchive(Path.Join(sourceDirectory.FullName, $"{mediaWithoutExtension}.zip"));

            foreach (var funscriptFile in sourceDirectory.EnumerateFiles($"{mediaWithoutExtension}*.funscript"))
                TryMatchFile(funscriptFile.Name, () => ScriptResource.FromFileInfo(funscriptFile));
        }

        return updated;
    }

    private void UpdateLinkedScriptsTo(DeviceAxis axis) => UpdateLinkScriptFor(DeviceAxis.All.Where(a => a != axis && AxisSettings[a].LinkAxis == axis));
    private void UpdateLinkScriptFor(IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        Logger.Debug("Trying to link axes [Axes: {list}]", axes);
        foreach (var axis in axes)
        {
            var model = AxisModels[axis];
            if (model.Settings.LinkAxis == null)
            {
                if (model.Settings.LinkAxisHasPriority)
                    ResetAxes(axis);

                continue;
            }

            if (model.Script != null)
            {
                if (model.Settings.LinkAxisHasPriority && model.Script.Origin == ScriptResourceOrigin.User)
                    continue;

                if (!model.Settings.LinkAxisHasPriority && model.Script.Origin != ScriptResourceOrigin.Link)
                    continue;
            }

            Logger.Debug("Linked {0} to {1}", axis.Name, model.Settings.LinkAxis.Name);

            SetScript(axis, ScriptResource.LinkTo(AxisModels[model.Settings.LinkAxis].Script));
        }
    }

    private void ResetAxes(params DeviceAxis[] axes) => ResetAxes(axes?.AsEnumerable());
    private void ResetAxes(IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        Logger.Debug("Resetting axes [Axes: {list}]", axes);
        foreach (var axis in axes)
        {
            if (AxisModels[axis].Script != null)
                ResetSync(true, axis);

            SetScript(axis, null);
        }
    }

    private void SetScript(DeviceAxis axis, IScriptResource script)
    {
        var model = AxisModels[axis];
        var state = AxisStates[axis];
        lock (state)
        {
            state.Invalidate();
            model.Script = script;
        }

        if (script != null)
            Logger.Info("Set {0} script to \"{1}\" from \"{2}\"", axis, script.Name, script.Source);

        UpdateLinkedScriptsTo(axis);
    }

    private void ReloadAxes(params DeviceAxis[] axes) => ReloadAxes(axes?.AsEnumerable());
    private void ReloadAxes(IEnumerable<DeviceAxis> axes = null)
    {
        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        ResetSync(true, axes);

        Logger.Debug("Reloading axes [Axes: {list}]", axes);
        var axesWithLinkPriority = axes.Where(a => AxisSettings[a].LinkAxisHasPriority);
        var axesWithoutLinkPriority = axes.Where(a => !AxisSettings[a].LinkAxisHasPriority);

        var updated = SearchForScripts(axesWithoutLinkPriority);
        UpdateLinkScriptFor(axesWithLinkPriority);
    }

    private void SkipGap(double minimumSkip = 0, params DeviceAxis[] axes) => SkipGap(axes?.AsEnumerable(), minimumSkip);
    private void SkipGap(IEnumerable<DeviceAxis> axes = null, double minimumSkip = 0)
    {
        double? GetSkipPosition(DeviceAxis axis)
        {
            var keyframes = AxisKeyframes[axis];
            if (keyframes == null)
                return null;

            var state = AxisStates[axis];
            var startIndex = state.InsideScript ? state.Index : 0;
            var skipIndex = keyframes.SkipGap(startIndex);
            if (skipIndex == startIndex && state.InsideScript)
                return null;

            if (!keyframes.ValidateIndex(skipIndex))
                return null;

            return keyframes[skipIndex].Position;
        }

        axes ??= DeviceAxis.All;
        if (!axes.Any())
            return;

        var maybeSkipPosition = AxisKeyframes.Keys.Select(a => GetSkipPosition(a)).MinBy(x => x ?? double.PositiveInfinity);
        var currentPosition = MediaPosition;
        if (maybeSkipPosition is not double skipPosition || currentPosition >= skipPosition || (skipPosition - currentPosition) <= minimumSkip)
            return;

        SeekMediaToTime(skipPosition);
    }
    #endregion

    #region Media
    public void OnOpenMediaLocation()
    {
        if (MediaResource == null)
            return;

        var fullPath = MediaResource.IsModified ? MediaResource.ModifiedPath : MediaResource.OriginalPath;
        if (MediaResource.IsUrl)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
        else
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (Directory.Exists(directory))
                Process.Start("explorer.exe", directory);
        }
    }

    public void OnPlayPauseClick()
    {
        _eventAggregator.Publish(new MediaPlayPauseMessage(!IsPlaying));
    }

    public void OnKeyframesHeatmapMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;
        if (e.ChangedButton != MouseButton.Left)
            return;

        SeekMediaToPercent(e.GetPosition(element).X / element.ActualWidth);
    }

    private void InvalidateMediaState()
    {
        MediaResource = null;
        MediaDuration = double.NaN;
        PlaybackSpeed = 1;
        SetMediaPositionInternal(double.NaN);
    }

    private void SetMediaPositionInternal(double position)
    {
        MediaPosition = position;
        _internalMediaPosition = position;
    }

    private void SeekMediaToPercent(double percent)
    {
        if (!double.IsFinite(MediaDuration) || !double.IsFinite(percent))
            return;

        _eventAggregator.Publish(new MediaSeekMessage(TimeSpan.FromSeconds(MediaDuration * MathUtils.Clamp01(percent))));
    }

    private void SeekMediaToTime(double time)
    {
        if (!double.IsFinite(MediaDuration) || !double.IsFinite(time))
            return;

        _eventAggregator.Publish(new MediaSeekMessage(TimeSpan.FromSeconds(MathUtils.Clamp(time, 0, MediaDuration))));
    }

    private Task _autoSkipToScriptStartTask = Task.CompletedTask;
    private void ScheduleAutoSkipToScriptStart()
    {
        if (!AutoSkipToScriptStartEnabled)
            return;

        if (!_autoSkipToScriptStartTask.IsCompleted)
            return;

        var token = _cancellationSource?.Token;
        if (token == null)
            return;

        _autoSkipToScriptStartTask = Task.Delay(1000, token.Value)
                                         .ContinueWith(_ => SeekMediaToScriptStart(AutoSkipToScriptStartOffset, onlyWhenBefore: true));
    }

    private void SeekMediaToScriptStart(double offset, bool onlyWhenBefore)
    {
        if (!double.IsFinite(MediaDuration) || !double.IsFinite(offset))
            return;

        var startPosition = AxisKeyframes.Select(x => x.Value)
                                         .NotNull()
                                         .Select(ks => ks.TryGet(ks.SkipGap(index: 0), out var k) ? k.Position : default(double?))
                                         .FirstOrDefault();
        if (startPosition == null)
            return;

        var targetMediaTime = Math.Max(Math.Min(startPosition.Value, MediaDuration) - offset, 0);
        if (onlyWhenBefore && targetMediaTime <= MediaPosition)
            return;

        Logger.Info("Skipping to script start at {0}s", targetMediaTime);
        SeekMediaToTime(targetMediaTime);
    }
    #endregion

    #region AxisSettings
    [SuppressPropertyChangedWarnings]
    public void OnAxisSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not AxisSettings settings)
            return;

        switch (e.PropertyName)
        {
            case nameof(ViewModels.AxisSettings.UpdateMotionProviderWhenPaused):
            case nameof(ViewModels.AxisSettings.UpdateMotionProviderWithoutScript):
            case nameof(ViewModels.AxisSettings.Inverted):
            case nameof(ViewModels.AxisSettings.Bypass):
                var (axis, _) = AxisSettings.FirstOrDefault(x => x.Value == settings);
                if (axis != null)
                    ResetSync(true, axis);
                break;
        }
    }

    public void OnAxisDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, model) = pair;
        var drop = e.Data.GetData(DataFormats.FileDrop);
        if (drop is IEnumerable<string> paths)
        {
            var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
            if (path == null)
                return;

            ResetSync(true, axis);
            SetScript(axis, ScriptResource.FromPath(path, userLoaded: true));
        }
    }

    public void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.Link;
    }

    public void OnAxisOpenFolder(DeviceAxis axis)
    {
        var model = AxisModels[axis];
        if (model.Script == null)
            return;

        var source = model.Script.Source;
        var path = Path.GetDirectoryName(source);
        if (!Directory.Exists(path))
            return;

        Process.Start("explorer.exe", path);
    }

    public void OnAxisLoad(DeviceAxis axis)
    {
        var dialog = new CommonOpenFileDialog()
        {
            InitialDirectory = Directory.Exists(MediaResource?.Source) ? MediaResource.Source : string.Empty,
            EnsureFileExists = true
        };
        dialog.Filters.Add(new CommonFileDialogFilter("Funscript files", "*.funscript"));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        ResetSync(true, axis);
        SetScript(axis, ScriptResource.FromFileInfo(new FileInfo(dialog.FileName), userLoaded: true));
    }

    public void OnAxisClear(DeviceAxis axis) => ResetAxes(axis);
    public void OnAxisReload(DeviceAxis axis) => ReloadAxes(axis);

    public void SetAxisValue(DeviceAxis axis, double value, bool offset = false)
    {
        if (axis == null)
            return;

        var state = AxisStates[axis];
        lock (state)
        {
            state.OverrideValue = offset
                ? MathUtils.Clamp01((double.IsFinite(state.Value) ? state.Value : axis.DefaultValue) + value)
                : value;
        }
    }

    private bool MoveScript(DeviceAxis axis, DirectoryInfo directory)
    {
        if (directory?.Exists == false || AxisModels[axis].Script == null)
            return false;

        try
        {
            var source = AxisModels[axis].Script.Source;
            if (!File.Exists(source))
                return false;

            File.Move(source, Path.Join(directory.FullName, Path.GetFileName(source)));
        }
        catch { return false; }

        return true;
    }

    public void OnAxisMoveToMedia(DeviceAxis axis)
    {
        if (MediaResource != null && MoveScript(axis, new DirectoryInfo(MediaResource.Source)))
            ReloadAxes(axis);
    }

    public RelayCommand<DeviceAxis, ScriptLibrary> OnAxisMoveToLibraryCommand => new(OnAxisMoveToLibrary);
    public void OnAxisMoveToLibrary(DeviceAxis axis, ScriptLibrary library)
    {
        if (MoveScript(axis, library?.Directory.AsRefreshed()))
            ReloadAxes(axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnLinkAxisPriorityChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, _) = pair;
        ReloadAxes(axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnSelectedLinkAxisChanged(object sender, SelectionChangedEventArgs e)
    {
        bool IsCircularLink(DeviceAxis from, DeviceAxis to)
        {
            var current = from;
            while (current != null)
            {
                if (current == to)
                {
                    Logger.Info("Found circular link [From: {0}, To: {1}]", from, to);
                    return true;
                }

                current = AxisSettings[current].LinkAxis;
            }

            return false;
        }

        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, model) = pair;
        if (e.AddedItems.TryGet<DeviceAxis>(0, out var added) && IsCircularLink(added, axis))
            model.Settings.LinkAxis = e.RemovedItems.TryGet<DeviceAxis>(0, out var removed) ? removed : null;

        ReloadAxes(axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnSelectedSmartLimitInputAxisChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, model) = pair;
        if (e.AddedItems.TryGet<DeviceAxis>(0, out var added) && axis == added)
            model.Settings.SmartLimitInputAxis = e.RemovedItems.TryGet<DeviceAxis>(0, out var removed) ? removed : null;

        ResetSync(true, axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnUpdateMotionProviderWithAxisChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, model) = pair;
        if (e.AddedItems.TryGet<DeviceAxis>(0, out var added) && axis == added)
            model.Settings.UpdateMotionProviderWithAxis = e.RemovedItems.TryGet<DeviceAxis>(0, out var removed) ? removed : null;

        ResetSync(true, axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnPreviewSelectedMotionProviderChanged(SelectionChangedEventArgs e)
    {
        if (e.Source is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, _) = pair;
        ResetSync(true, axis);
    }

    [SuppressPropertyChangedWarnings]
    public void OnScriptScaleSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, _) = pair;
        ResetSync(true, axis);
    }
    #endregion

    #region MediaResource
    public async void OnMediaPathModifierConfigure(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        _ = await DialogHost.Show(modifier, "MediaPathModifierDialog").ConfigureAwait(true);

        if (MediaResource != null)
            Handle(new MediaPathChangedMessage(MediaResource.OriginalPath));
    }

    public void OnMediaPathModifierAdd(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<string, Type> pair)
            return;

        var (_, type) = pair;
        var modifier = (IMediaPathModifier)Activator.CreateInstance(type);
        MediaPathModifiers.Add(modifier);

        if (MediaResource != null)
            Handle(new MediaPathChangedMessage(MediaResource.OriginalPath));
    }

    public void OnMediaPathModifierRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        MediaPathModifiers.Remove(modifier);

        if (MediaResource != null)
            Handle(new MediaPathChangedMessage(MediaResource.OriginalPath));
    }

    public void OnMapCurrentMediaPathToFile(object sender, RoutedEventArgs e)
    {
        if (MediaResource?.IsUrl != true)
            return;

        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            EnsureFileExists = true
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        MediaPathModifiers.Add(new FindReplaceMediaPathModifierViewModel()
        {
            Find = MediaResource.OriginalPath,
            Replace = dialog.FileName
        });

        Handle(new MediaPathChangedMessage(MediaResource.OriginalPath));
    }
    #endregion

    #region ScriptLibrary
    public void OnLibraryAdd(object sender, RoutedEventArgs e)
    {
        //TODO: remove dependency once /dotnet/wpf/issues/438 is resolved
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var directory = new DirectoryInfo(dialog.FileName);
        ScriptLibraries.Add(new ScriptLibrary(directory));
        ReloadAxes(null);
    }

    public void OnLibraryDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
            return;

        ScriptLibraries.Remove(library);
        ReloadAxes(null);
    }

    public void OnLibraryOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
            return;

        Process.Start("explorer.exe", library.Directory.FullName);
    }
    #endregion

    #region Shortcuts
    public void RegisterShortcuts(IShortcutManager s)
    {
        void UpdateSettings(DeviceAxis axis, Action<AxisSettings> callback)
        {
            if (axis != null)
                callback(AxisSettings[axis]);
        }

        #region Media::PlayPause
        s.RegisterAction("Media::PlayPause::Set",
            b => b.WithSetting<bool>(p => p.WithLabel("Play"))
                  .WithCallback((_, play) =>
                  {
                      if (play && !IsPlaying) OnPlayPauseClick();
                      else if (!play && IsPlaying) OnPlayPauseClick();
                  }));

        s.RegisterAction("Media::PlayPause::Toggle", b => b.WithCallback(_ => OnPlayPauseClick()));
        #endregion

        #region Media::ScriptOffset
        s.RegisterAction("Media::ScriptOffset::Offset", b => b.WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                                                              .WithCallback((_, offset) => GlobalOffset += offset));
        s.RegisterAction("Media::ScriptOffset::Set", b => b.WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                                                           .WithCallback((_, value) => GlobalOffset = value));
        #endregion

        #region Media::Position
        s.RegisterAction("Media::Position::Time::Offset", b => b.WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                                                                .WithCallback((_, offset) => SeekMediaToTime((float)(MediaPosition + offset))));
        s.RegisterAction("Media::Position::Time::Set", b => b.WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                                                             .WithCallback((_, value) => SeekMediaToTime(value)));

        s.RegisterAction("Media::Position::Percent::Offset", b => b.WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                                                                   .WithCallback((_, offset) => SeekMediaToPercent((float)(MediaPosition / MediaDuration + offset / 100))));
        s.RegisterAction("Media::Position::Percent::Set", b => b.WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                                                                .WithCallback((_, value) => SeekMediaToPercent(value / 100)));

        s.RegisterAction("Media::Position::SkipToScriptStart", b => b.WithSetting<float>(p => p.WithLabel("Offset").WithStringFormat("{}{0}s"))
                                                                     .WithCallback((_, offset) => SeekMediaToScriptStart(offset, onlyWhenBefore: false)));
        #endregion

        #region Media::AutoSkipToScriptStartEnabled
        s.RegisterAction("Media::AutoSkipToScriptStartEnabled::Set",
            b => b.WithSetting<bool>(p => p.WithLabel("Auto-skip to script start enabled"))
                  .WithCallback((_, enabled) => AutoSkipToScriptStartEnabled = enabled));

        s.RegisterAction("Media::AutoSkipToScriptStartEnabled::Toggle",
            b => b.WithCallback(_ => AutoSkipToScriptStartEnabled = !AutoSkipToScriptStartEnabled));
        #endregion

        #region Media::AutoSkipToScriptStartOffset
        s.RegisterAction("Media::AutoSkipToScriptStartOffset::Set",
            b => b.WithSetting<float>(p => p.WithLabel("Script start auto-skip offset").WithStringFormat("{}{0}s"))
                  .WithCallback((_, offset) => AutoSkipToScriptStartOffset = offset));
        #endregion

        #region Script::SkipGap
        s.RegisterAction("Script::SkipGap",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target").WithItemsSource(DeviceAxis.All).WithDescription("Target axis script to check for gaps\nEmpty to check all scripts"))
                  .WithSetting<float>(p => p.WithLabel("Minimum skip").WithDefaultValue(2).WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, minimumSkip) => SkipGap(minimumSkip, axis)));
        #endregion

        #region Axis::Value
        s.RegisterAction("Axis::Value::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0:P0}"))
                  .WithCallback((_, axis, offset) => SetAxisValue(axis, offset, offset: true)));

        s.RegisterAction("Axis::Value::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0:P0}"))
                  .WithCallback((_, axis, value) => SetAxisValue(axis, value)));

        s.RegisterAction("Axis::Value::Drive",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((gesture, axis) =>
                  {
                      if (gesture is IAxisInputGesture axisGesture)
                          SetAxisValue(axis, axisGesture.Delta, offset: true);
                  }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::Sync
        s.RegisterAction("Axis::Sync",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => { if (axis != null) ResetSync(true, axis); }));

        s.RegisterAction("Axis::SyncAll",
            b => b.WithCallback(_ => ResetSync(true, null)));
        #endregion

        #region Axis::Bypass
        s.RegisterAction("Axis::Bypass::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Bypass"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.Bypass = enabled)));

        s.RegisterAction("Axis::Bypass::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.Bypass = !s.Bypass)));
        #endregion

        #region Axis::ClearScript
        s.RegisterAction("Axis::ClearScript",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => { if (axis != null) OnAxisClear(axis); }));
        #endregion

        #region Axis::ReloadScript
        s.RegisterAction("Axis::ReloadScript",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => { if (axis != null) OnAxisReload(axis); }));
        #endregion

        #region Axis::Inverted
        s.RegisterAction("Axis::Inverted::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Invert"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.Inverted = enabled)));

        s.RegisterAction("Axis::Inverted::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.Inverted = !s.Inverted)));
        #endregion

        #region Axis::LinkPriority
        s.RegisterAction("Axis::LinkPriority::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Link has priority").WithDescription("When enabled the link has priority\nover automatically loaded scripts"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.LinkAxisHasPriority = enabled)));

        s.RegisterAction("Axis::LinkPriority::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.LinkAxisHasPriority = !s.LinkAxisHasPriority)));
        #endregion

        #region Axis::SmartLimitInputAxis
        s.RegisterAction("Axis::SmartLimitInputAxis::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<DeviceAxis>(p => p.WithLabel("Input axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, source, input) =>
                  {
                      if (source == null || source == input)
                          return;

                      ResetSync(true, source);
                      AxisSettings[source].SmartLimitInputAxis = input;
                  }));
        #endregion

        #region Axis::LinkAxis
        s.RegisterAction("Axis::LinkAxis::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Source axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, source, target) =>
                  {
                      if (source == null || source == target)
                          return;

                      AxisSettings[source].LinkAxis = target;
                      ReloadAxes(source);
                  }));
        #endregion

        #region Axis::SpeedLimitEnabled
        s.RegisterAction("Axis::SpeedLimitEnabled::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Speed limit enabled"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.SpeedLimitEnabled = enabled)));

        s.RegisterAction("Axis::SpeedLimitEnabled::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.SpeedLimitEnabled = !s.SpeedLimitEnabled)));
        #endregion

        #region Axis::SpeedLimitSecondsPerStroke
        s.RegisterAction("Axis::SpeedLimitSecondsPerStroke::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0:F3}s/stroke"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = MathUtils.Clamp(s.MaximumSecondsPerStroke + offset, 0.001, 2))));

        s.RegisterAction("Axis::SpeedLimitSecondsPerStroke::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0:F3}s/stroke"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = MathUtils.Clamp(value, 0.001, 2))));
        #endregion

        #region Axis::AutoHomeEnabled
        s.RegisterAction("Axis::AutoHomeEnabled::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Auto home enabled"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.AutoHomeEnabled = enabled)));

        s.RegisterAction("Axis::AutoHomeEnabled::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.AutoHomeEnabled = !s.AutoHomeEnabled)));
        #endregion

        #region Axis::AutoHomeDelay
        s.RegisterAction("Axis::AutoHomeDelay::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.AutoHomeDelay = Math.Max(0, s.AutoHomeDelay + offset))));

        s.RegisterAction("Axis::AutoHomeDelay::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.AutoHomeDelay = MathF.Max(0, value))));
        #endregion

        #region Axis::AutoHomeDuration
        s.RegisterAction("Axis::AutoHomeDuration::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.AutoHomeDuration = Math.Max(0, s.AutoHomeDuration + offset))));

        s.RegisterAction("Axis::AutoHomeDuration::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.AutoHomeDuration = MathF.Max(0, value))));
        #endregion

        #region Axis::ScriptOffset
        s.RegisterAction("Axis::ScriptOffset::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.Offset += offset)));

        s.RegisterAction("Axis::ScriptOffset::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.Offset = value)));
        #endregion

        #region Axis::ScriptScale
        s.RegisterAction("Axis::ScriptScale::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.Scale = MathUtils.Clamp(s.Scale + offset, 1, 400))));

        s.RegisterAction("Axis::ScriptScale::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.Scale = MathUtils.Clamp(value, 1, 400))));
        #endregion

        #region Axis::MotionProvider
        var motionProviderNames = MotionProviderManager.MotionProviderNames;
        s.RegisterAction("Axis::MotionProvider::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<string>(p => p.WithLabel("Motion provider").WithItemsSource(motionProviderNames))
                  .WithCallback((_, axis, motionProviderName) =>
                        UpdateSettings(axis, s => s.SelectedMotionProvider = motionProviderNames.Contains(motionProviderName) ? motionProviderName : null)));

        MotionProviderManager.RegisterShortcuts(s);
        #endregion

        #region Axis::MotionProviderBlend
        s.RegisterAction("Axis::MotionProviderBlend::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset"))
                  .WithCallback((_, axis, offset) =>
                        UpdateSettings(axis, s => s.MotionProviderBlend = MathUtils.Clamp(s.MotionProviderBlend + offset, 0, 100))));

        s.RegisterAction("Axis::MotionProviderBlend::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, value) =>
                        UpdateSettings(axis, s => s.MotionProviderBlend = MathUtils.Clamp(value, 0, 100))));

        s.RegisterAction("Axis::MotionProviderBlend::Drive",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((gesture, axis) =>
                  {
                      if (gesture is IAxisInputGesture axisGesture)
                          UpdateSettings(axis, s => s.MotionProviderBlend = MathUtils.Clamp(s.MotionProviderBlend + axisGesture.Delta * 100, 0, 100));
                  }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
        #endregion

        #region Axis::UpdateMotionProviderWhenPaused
        s.RegisterAction("Axis::UpdateMotionProviderWhenPaused::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Update motion providers when paused"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.UpdateMotionProviderWhenPaused = enabled)));

        s.RegisterAction("Axis::UpdateMotionProviderWhenPaused::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.UpdateMotionProviderWhenPaused = !s.UpdateMotionProviderWhenPaused)));
        #endregion

        #region Axis::UpdateMotionProviderWithoutScript
        s.RegisterAction("Axis::UpdateMotionProviderWithoutScript::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<bool>(p => p.WithLabel("Update motion providers without script"))
                  .WithCallback((_, axis, enabled) => UpdateSettings(axis, s => s.UpdateMotionProviderWithoutScript = enabled)));

        s.RegisterAction("Axis::UpdateMotionProviderWithoutScript::Toggle",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithCallback((_, axis) => UpdateSettings(axis, s => s.UpdateMotionProviderWithoutScript = !s.UpdateMotionProviderWithoutScript)));
        #endregion
    }
    #endregion

    protected virtual void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();
        _updateThread?.Join();
        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _updateThread = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class AxisModel : PropertyChangedBase
{
    public AxisState State { get; } = new AxisState();
    public AxisSettings Settings { get; } = new AxisSettings();
    public IScriptResource Script { get; set; } = null;
}

public class AxisState : INotifyPropertyChanged
{
    [DoNotNotify] public int Index { get; set; } = int.MinValue;
    [DoNotNotify] public double Value { get; set; } = double.NaN;
    [DoNotNotify] public double OverrideValue { get; set; } = double.NaN;

    [DoNotNotify] public bool Invalid => Index == int.MinValue;
    [DoNotNotify] public bool BeforeScript => Index == -1;
    [DoNotNotify] public bool AfterScript => Index == int.MaxValue;
    [DoNotNotify] public bool InsideScript => Index >= 0 && Index != int.MaxValue;

    [DoNotNotify] public bool InsideGap { get; set; } = false;

    [DoNotNotify] public double SyncTime { get; set; } = 0;
    [DoNotNotify] public double AutoHomeTime { get; set; } = 0;

    [DoNotNotify] public bool IsDirty { get; set; } = true;
    [DoNotNotify] public bool IsAutoHoming { get; set; } = false;

    public bool IsSpeedLimited { get; set; } = false;
    public bool IsSmartLimited { get; set; } = false;

    public event PropertyChangedEventHandler PropertyChanged;

    public void Invalidate(bool end = false) => Index = end ? int.MaxValue : int.MinValue;

    public void NotifyValueChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InsideScript)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}

[JsonObject(MemberSerialization.OptIn)]
public class AxisSettings : PropertyChangedBase
{
    [JsonProperty] public bool LinkAxisHasPriority { get; set; } = false;
    [JsonProperty] public DeviceAxis LinkAxis { get; set; } = null;

    [JsonProperty] public DeviceAxis SmartLimitInputAxis { get; set; } = null;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableConcurrentCollection<Point> SmartLimitPoints { get; set; } = new ObservableConcurrentCollection<Point>();
    [JsonProperty] public SmartLimitMode SmartLimitMode { get; set; } = SmartLimitMode.Value;
    [JsonProperty] public double SmartLimitTargetValue { get; set; } = 0.5;

    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;
    [JsonProperty] public bool AutoHomeEnabled { get; set; } = false;
    [JsonProperty] public double AutoHomeDelay { get; set; } = 5;
    [JsonProperty] public double AutoHomeDuration { get; set; } = 3;
    [JsonProperty] public bool Inverted { get; set; } = false;
    [JsonProperty] public double Offset { get; set; } = 0;
    [JsonProperty] public double Scale { get; set; } = 100;
    [JsonProperty] public bool Bypass { get; set; } = false;
    [JsonProperty] public double MotionProviderBlend { get; set; } = 100;
    [JsonProperty] public bool MotionProviderFillGaps { get; set; } = false;
    [JsonProperty] public double MotionProviderMinimumGapDuration { get; set; } = 5;
    [JsonProperty] public bool UpdateMotionProviderWhenPaused { get; set; } = false;
    [JsonProperty] public bool UpdateMotionProviderWithoutScript { get; set; } = true;
    [JsonProperty] public DeviceAxis UpdateMotionProviderWithAxis { get; set; } = null;
    [JsonProperty] public string SelectedMotionProvider { get; set; } = null;
    [JsonProperty] public bool SpeedLimitEnabled { get; set; } = false;
    [JsonProperty] public double MaximumSecondsPerStroke { get; set; } = 0.1;
}

public enum SmartLimitMode
{
    Value,
    Speed
}

[JsonObject(MemberSerialization.OptIn)]
public class SyncSettings : PropertyChangedBase
{
    [JsonProperty] public double Duration { get; set; } = 4;
    [JsonProperty] public bool SyncOnMediaResourceChanged { get; set; } = true;
    [JsonProperty] public bool SyncOnMediaPlayPause { get; set; } = true;
    [JsonProperty] public bool SyncOnSeek { get; set; } = true;
}

[JsonObject(MemberSerialization.OptIn)]
public class ScriptLibrary : PropertyChangedBase
{
    public ScriptLibrary(DirectoryInfo directory)
    {
        Directory = directory;
    }

    [JsonProperty] public DirectoryInfo Directory { get; }
    [JsonProperty] public bool Recursive { get; set; }

    public IEnumerable<FileInfo> EnumerateFiles(string searchPattern) => Directory.SafeEnumerateFiles(searchPattern, IOUtils.CreateEnumerationOptions(Recursive));
}
