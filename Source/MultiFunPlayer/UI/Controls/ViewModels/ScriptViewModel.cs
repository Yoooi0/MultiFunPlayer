using MultiFunPlayer.Common;
using Stylet;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows;
using PropertyChanged;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
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
using MultiFunPlayer.MotionProvider.ViewModels;
using System.Windows.Controls.Primitives;
using MultiFunPlayer.Property;
using Microsoft.Win32;
using MultiFunPlayer.Script;
using MultiFunPlayer.Script.Repository;
using MultiFunPlayer.Shortcut;

namespace MultiFunPlayer.UI.Controls.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class ScriptViewModel : Screen, IDeviceAxisValueProvider, IDisposable,
    IHandle<MediaPositionChangedMessage>, IHandle<MediaPlayingChangedMessage>, IHandle<MediaPathChangedMessage>, IHandle<MediaDurationChangedMessage>,
    IHandle<MediaSpeedChangedMessage>, IHandle<SettingsMessage>, IHandle<SyncRequestMessage>, IHandle<ReloadScriptsRequestMessage>, IHandle<ChangeScriptMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;

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
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, ChapterCollection> AxisChapters { get; }
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, BookmarkCollection> AxisBookmarks { get; }

    public Dictionary<string, Type> MediaPathModifierTypes { get; }
    public MediaLoopSegment MediaLoopSegment { get; }

    public IScriptRepositoryManager ScriptRepositoryManager { get; }
    public IMotionProviderManager MotionProviderManager { get; }
    public MediaResourceInfo MediaResource { get; set; }

    [JsonProperty] public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisSettings> AxisSettings { get; }
    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public ObservableConcurrentCollection<IMediaPathModifier> MediaPathModifiers { get; }
    [JsonProperty] public SyncSettings SyncSettings { get; set; }

    [JsonProperty] public double GlobalOffset { get; set; } = 0;
    [JsonProperty] public bool ValuesContentVisible { get; set; } = false;
    [JsonProperty] public bool MediaContentVisible { get; set; } = false;
    [JsonProperty] public bool AxisContentVisible { get; set; } = false;
    [JsonProperty] public bool HeatmapShowRange { get; set; } = true;
    [JsonProperty] public bool HeatmapEnablePreview { get; set; } = true;
    [JsonProperty] public bool HeatmapCombineHeat { get; set; } = true;
    [JsonProperty] public int HeatmapBucketCount { get; set; } = 333;
    [JsonProperty] public bool HeatmapInvertY { get; set; } = false;
    [JsonProperty] public bool AutoSkipToScriptStartEnabled { get; set; } = true;
    [JsonProperty] public double AutoSkipToScriptStartOffset { get; set; } = 5;

    public bool IsSyncing => AxisStates.Values.Any(s => s.SyncTime > 0);
    public double SyncProgress => !IsSyncing ? 100 : GetSyncProgress(AxisStates.Values.Max(s => s.SyncTime), SyncSettings.Duration) * 100;

    public ScriptViewModel(IShortcutManager shortcutManager, IPropertyManager propertyManager, IMotionProviderManager motionProviderManager,
        IScriptRepositoryManager scriptManager, IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);
        ScriptRepositoryManager = scriptManager;

        MotionProviderManager = motionProviderManager;

        AxisModels = new ObservableConcurrentDictionary<DeviceAxis, AxisModel>(DeviceAxis.All.ToDictionary(a => a, a => new AxisModel(a)));
        MediaPathModifiers = [];
        MediaPathModifierTypes = ReflectionUtils.FindImplementations<IMediaPathModifier>()
                                                .ToDictionary(t => t.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName, t => t);

        MediaLoopSegment = new MediaLoopSegment();
        SyncSettings = new SyncSettings();

        InvalidateMediaState();

        IsPlaying = false;

        AxisStates = AxisModels.CreateView(model => model.State);
        AxisSettings = AxisModels.CreateView(model => model.Settings);
        AxisKeyframes = AxisModels.CreateView(model => model.Script?.Keyframes, "Script");
        AxisChapters = AxisModels.CreateView(model => model.Script?.Chapters, "Script");
        AxisBookmarks = AxisModels.CreateView(model => model.Script?.Bookmarks, "Script");

        foreach (var (_, settings) in AxisSettings)
            settings.PropertyChanged += OnAxisSettingsPropertyChanged;

        _cancellationSource = new CancellationTokenSource();
        _updateThread = new Thread(() => UpdateThread(_cancellationSource.Token)) { IsBackground = true };
        _updateThread.Start();

        ResetSync(false);
        RegisterActions(shortcutManager);
        RegisterProperties(propertyManager);
    }

    private void UpdateThread(CancellationToken token)
    {
        const double uiUpdateInterval = 1d / 60d;
        const double mediaLoopUpdateInterval = 1d / 10d;

        var contexts = DeviceAxis.All.ToDictionary(a => a, a => new AxisStateUpdateContext(AxisModels[a]));

        var uiUpdateTime = 0d;
        var mediaLoopUpdateTime = 0d;
        var deltaTime = 0d;

        var stopwatch = Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            var dirty = UpdateValues();
            UpdateUi();
            UpdateMediaLoop();

            stopwatch.SleepPrecise(IsPlaying || dirty ? 2 : 10);
            deltaTime = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();
        }

        bool UpdateValues()
        {
            if (IsPlaying)
            {
                _internalMediaPosition += deltaTime * PlaybackSpeed;

                var error = _internalMediaPosition - MediaPosition;
                MediaPosition += Math.Clamp(error, deltaTime * PlaybackSpeed * 0.9, deltaTime * PlaybackSpeed * 1.1);
            }

            foreach (var axis in DeviceAxis.All)
                contexts[axis].BeginUpdate();

            var dirty = false;
            foreach (var axis in DeviceAxis.All)
            {
                var state = AxisStates[axis];
                var settings = AxisSettings[axis];
                var context = contexts[axis];

                dirty |= UpdateValuesInternal(axis, state, settings, context);
                dirty |= CalculateFinalValue(axis, state, settings, context);
            }

            foreach (var axis in DeviceAxis.All)
                contexts[axis].EndUpdate(deltaTime);

            return dirty;

            bool UpdateValuesInternal(DeviceAxis axis, AxisState state, AxisSettings settings, AxisStateUpdateContext context)
            {
                var dirty = false;
                dirty |= UpdateScript();
                dirty |= UpdateMotionProvider();
                dirty |= UpdateTransition();

                return dirty;

                bool UpdateScript()
                {
                    bool NoUpdate()
                    {
                        context.InsideGap = false;
                        return false;
                    }

                    if (settings.BypassScript)
                        return NoUpdate();

                    if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                        return NoUpdate();

                    var axisPosition = GetAxisPosition(axis);
                    var shouldSearch = context.Invalid
                                   || (keyframes.ValidateIndex(context.Index) && keyframes[context.Index].Position > axisPosition)
                                   || (context.AfterScript && keyframes[^1].Position > axisPosition);

                    if (shouldSearch)
                    {
                        Logger.Debug("Searching for valid index [Axis: {0}]", axis);
                        context.Index = keyframes.SearchForIndexBefore(axisPosition);
                    }

                    if (context.AfterScript)
                        return NoUpdate();

                    var beforeIndex = context.Index;
                    context.Index = keyframes.AdvanceIndex(context.Index, axisPosition);

                    if (beforeIndex == AxisState.BeforeScriptIndex && context.Index >= 0)
                    {
                        Logger.Debug("Resetting sync on script start [Axis: {0}]", axis);
                        ResetSyncNoLock(state);
                    }

                    if (!keyframes.ValidateIndex(context.Index) || !keyframes.ValidateIndex(context.Index + 1))
                    {
                        if (context.Index + 1 >= keyframes.Count)
                        {
                            Logger.Debug("Resetting sync on script end [Axis: {0}]", axis);
                            context.Index = AxisState.AfterScriptIndex;
                            ResetSyncNoLock(state);
                        }

                        return NoUpdate();
                    }

                    if (context.BeforeScript)
                        return NoUpdate();

                    context.InsideGap = keyframes.IsGap(context.Index);
                    var scriptValue = MathUtils.Clamp01(keyframes.Interpolate(context.Index, axisPosition, settings.InterpolationType));
                    context.ScriptValue = MathUtils.Clamp01(axis.DefaultValue + (scriptValue - axis.DefaultValue) * settings.ScriptScale / 100);
                    return context.IsScriptDirty;
                }

                bool UpdateMotionProvider()
                {
                    if (settings.SelectedMotionProvider == null)
                        return false;

                    if (settings.BypassMotionProvider)
                        return false;

                    bool ShouldUpdateMotionProvider(bool canFillGap)
                    {
                        if (!settings.UpdateMotionProviderWhenPaused && !IsPlaying)
                            return false;
                        if (!canFillGap && !settings.UpdateMotionProviderWithoutScript && !context.InsideScript)
                            return false;

                        if (settings.UpdateMotionProviderWithAxis != null)
                        {
                            var targetState = AxisStates[settings.UpdateMotionProviderWithAxis];
                            if (!targetState.IsDirty || targetState.IsAutoHoming)
                                return false;
                        }

                        return true;
                    }

                    bool CanMotionProviderFillGap()
                    {
                        if (!settings.MotionProviderFillGaps)
                            return false;
                        if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                            return false;

                        if (context.InsideGap && keyframes.GapDuration(context.Index) >= settings.MotionProviderMinimumGapDuration)
                        {
                            if (!context.LastInsideGap)
                                ResetSyncNoLock(state);

                            return true;
                        }

                        if (context.LastInsideGap && keyframes.GapDuration(context.LastIndex) >= settings.MotionProviderMinimumGapDuration)
                            ResetSyncNoLock(state);

                        return false;
                    }

                    var isGapFill = CanMotionProviderFillGap();
                    if (ShouldUpdateMotionProvider(isGapFill))
                        MotionProviderManager.Update(axis, settings.SelectedMotionProvider, deltaTime);

                    var providerValue = MotionProviderManager.GetValue(axis);
                    var blendT = context.InsideScript && !isGapFill ? MathUtils.Clamp01(settings.MotionProviderBlend / 100) : 1;
                    var blendFrom = double.IsFinite(context.ScriptValue) ? context.ScriptValue : axis.DefaultValue;
                    providerValue = MathUtils.Clamp01(MathUtils.Lerp(blendFrom, providerValue, blendT));

                    if (!double.IsFinite(providerValue))
                        return false;

                    context.MotionProviderValue = providerValue;
                    return context.IsMotionProviderDirty;
                }

                bool UpdateTransition()
                {
                    if (settings.BypassTransition)
                        return false;
                    if (!state.ExternalTransition.Update(deltaTime))
                        return false;

                    context.TransitionValue = state.ExternalTransition.GetValue();
                    return context.IsTransitionDirty;
                }
            }

            bool CalculateFinalValue(DeviceAxis axis, AxisState state, AxisSettings settings, AxisStateUpdateContext context)
            {
                context.Value = context.LastValue;

                var autoHomeAllowed = CheckIfAutoHomeAllowed();
                if (!autoHomeAllowed)
                    ResetAutoHome();

                ApplyValues();
                if (autoHomeAllowed)
                    UpdateAutoHome();

                if (SyncSettings.SyncOnAutoHomeStartEnd)
                    if (context.LastIsAutoHoming && !context.IsAutoHoming)
                        ResetSyncNoLock(state);

                UpdateSync();
                UpdateSmartLimit();
                SpeedLimit();

                return context.IsDirty;

                void ApplyValues()
                {
                    if (double.IsFinite(context.ScriptValue))
                        context.Value = context.ScriptValue;

                    if (double.IsFinite(context.MotionProviderValue))
                    {
                        bool SpeedLimitMotionProviderWithAxis(out double value)
                        {
                            value = double.NaN;
                            if (!settings.MotionProviderSpeedLimitWithAxis)
                                return false;
                            if (settings.UpdateMotionProviderWithAxis == null)
                                return false;

                            var targetState = AxisStates[settings.UpdateMotionProviderWithAxis];
                            var maxSpeed = Math.Abs(targetState.Speed);
                            if (!double.IsFinite(maxSpeed))
                                return false;

                            var step = context.MotionProviderValue - context.LastValue;
                            if (!double.IsFinite(step))
                                return false;

                            var direction = Math.Sign(step);
                            var speed = Math.Abs(step / deltaTime);
                            if (speed <= maxSpeed)
                                return false;

                            value = MathUtils.Clamp01(context.LastValue + maxSpeed * deltaTime * direction);
                            return true;
                        }

                        context.Value = SpeedLimitMotionProviderWithAxis(out var value) ? value : context.MotionProviderValue;
                    }

                    if (double.IsFinite(context.TransitionValue))
                        if (!IsPlaying && !context.InsideScript)
                            if (!context.IsScriptDirty && !context.IsMotionProviderDirty)
                                context.Value = context.TransitionValue;

                    if (settings.InvertValue)
                        context.Value = 1 - context.Value;
                }

                void UpdateSync()
                {
                    if (state.SyncTime <= 0)
                        return;

                    var t = GetSyncProgress(state.SyncTime, SyncSettings.Duration);

                    state.SyncTime -= deltaTime;
                    if (context.IsAutoHoming || !double.IsFinite(context.Value))
                        return;

                    var from = !double.IsFinite(context.LastValue) ? axis.DefaultValue : context.LastValue;
                    context.Value = MathUtils.Clamp01(MathUtils.Lerp(from, context.Value, t));
                }

                bool CheckIfAutoHomeAllowed()
                {
                    if (!double.IsFinite(context.Value) || !settings.AutoHomeEnabled)
                        return false;
                    if (!settings.AutoHomeInsideScript && context.InsideScript && IsPlaying)
                        return false;
                    if (context.IsScriptDirty || context.IsMotionProviderDirty)
                        return false;
                    if (context.IsTransitionDirty && !(IsPlaying || context.InsideScript))
                        return false;

                    return true;
                }

                void ResetAutoHome()
                {
                    state.AutoHomeTime = 0;
                    context.IsAutoHoming = false;
                }

                void UpdateAutoHome()
                {
                    bool UpdateAutoHomeInternal()
                    {
                        state.AutoHomeTime += deltaTime;
                        var t = (settings.AutoHomeDelay, settings.AutoHomeDuration) switch
                        {
                            (< 0.001, < 0.001) => 1,
                            (< 0.001, _) => state.AutoHomeTime / settings.AutoHomeDuration,
                            (_, < 0.001) => state.AutoHomeTime <= settings.AutoHomeDelay ? 0 : 1,
                            (_, _) => (state.AutoHomeTime - settings.AutoHomeDelay) / settings.AutoHomeDuration
                        };

                        if (t < 0) return false;
                        if (t == 0) return true;
                        if (t >= 1 && Math.Abs(context.Value - settings.AutoHomeTargetValue) < 0.00001)
                        {
                            context.Value = settings.AutoHomeTargetValue;
                            return true;
                        }

                        context.Value = MathUtils.Clamp01(MathUtils.Lerp(context.Value, settings.AutoHomeTargetValue, t * Math.Pow(2, 8 * (t - 1))));
                        return true;
                    }

                    context.IsAutoHoming = UpdateAutoHomeInternal();
                }

                void UpdateSmartLimit()
                {
                    bool UpdateSmartLimitInternal()
                    {
                        if (settings.SmartLimitInputAxis == null)
                            return false;
                        if (!double.IsFinite(context.Value))
                            return false;
                        if (settings.SmartLimitPoints == null || settings.SmartLimitPoints.Count == 0)
                            return false;

                        var x = AxisStates[settings.SmartLimitInputAxis].Value * 100;
                        if (!double.IsFinite(x))
                            return false;

                        var factor = Interpolation.Linear(settings.SmartLimitPoints, p => p.X, p => p.Y, x) / 100;

                        context.Value = settings.SmartLimitMode switch
                        {
                            SmartLimitMode.Value => MathUtils.Clamp01(MathUtils.Lerp(settings.SmartLimitTargetValue, context.Value, factor)),
                            SmartLimitMode.Speed when double.IsFinite(context.LastValue) => MathUtils.Clamp01(MathUtils.Lerp(context.LastValue, context.Value, Math.Pow(factor, 4))),
                            _ => context.Value
                        };

                        return factor < 1;
                    }

                    context.IsSmartLimited = UpdateSmartLimitInternal();
                }

                void SpeedLimit()
                {
                    bool SpeedLimitInternal()
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

                    context.IsSpeedLimited = SpeedLimitInternal();
                }
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

        void UpdateMediaLoop()
        {
            mediaLoopUpdateTime += deltaTime;
            if (mediaLoopUpdateTime < mediaLoopUpdateInterval)
                return;

            mediaLoopUpdateTime = 0;
            if (MediaLoopSegment.TryGetPositions(out var startPosition, out var endPosition))
                if (MediaPosition >= endPosition)
                    SeekMediaToTime(startPosition);
        }
    }

    #region Events
    public void Handle(MediaPathChangedMessage message)
    {
        var builder = new MediaResourceInfoBuilder(message.Path);
        builder.WithModifiers(MediaPathModifiers);

        var resource = builder.Build();
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
        if (message.ReloadScripts)
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
        if (MediaDuration != newDuration)
        {
            Logger.Info("Received {0} [Duration: {1}]", nameof(MediaDurationChangedMessage), message.Duration?.ToString());
            MediaDuration = newDuration;
        }

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
        var wasSeek = Math.Abs(error) > 1.0 || message.ForceSeek;
        if (wasSeek)
        {
            Logger.Debug("Detected seek: {0}", error);
            if (SyncSettings.SyncOnSeek)
                ResetSync();

            if (newPosition <= MediaLoopSegment.StartPosition - 1 || newPosition >= MediaLoopSegment.EndPosition + 1)
                ClearMediaLoop();
            UpdateCurrentPosition(newPosition);
        }
        else
        {
            _internalMediaPosition += 0.33 * (newPosition - _internalMediaPosition);
        }
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Script")
             || !message.Settings.TryGetObject(out var settings, "Script"))
                return;

            settings.Merge(JObject.FromObject(this), new JsonMergeSettings() { MergeArrayHandling = MergeArrayHandling.Replace });
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
                MediaPathModifiers.SetFrom(mediaPathModifiers);

            if (settings.TryGetValue<double>(nameof(GlobalOffset), out var globalOffset)) GlobalOffset = globalOffset;
            if (settings.TryGetValue<bool>(nameof(ValuesContentVisible), out var valuesContentVisible)) ValuesContentVisible = valuesContentVisible;
            if (settings.TryGetValue<bool>(nameof(MediaContentVisible), out var mediaContentVisible)) MediaContentVisible = mediaContentVisible;
            if (settings.TryGetValue<bool>(nameof(AxisContentVisible), out var axisContentVisible)) AxisContentVisible = axisContentVisible;
            if (settings.TryGetValue<int>(nameof(HeatmapBucketCount), out var heatmapBucketCount)) HeatmapBucketCount = heatmapBucketCount;
            if (settings.TryGetValue<bool>(nameof(HeatmapEnablePreview), out var heatmapEnablePreview)) HeatmapEnablePreview = heatmapEnablePreview;
            if (settings.TryGetValue<bool>(nameof(HeatmapCombineHeat), out var heatmapCombineHeat)) HeatmapCombineHeat = heatmapCombineHeat;
            if (settings.TryGetValue<bool>(nameof(HeatmapInvertY), out var heatmapInvertY)) HeatmapInvertY = heatmapInvertY;
            if (settings.TryGetValue<bool>(nameof(HeatmapShowRange), out var heatmapShowRange)) HeatmapShowRange = heatmapShowRange;
            if (settings.TryGetValue<bool>(nameof(AutoSkipToScriptStartEnabled), out var autoSkipToScriptStartEnabled)) AutoSkipToScriptStartEnabled = autoSkipToScriptStartEnabled;
            if (settings.TryGetValue<double>(nameof(AutoSkipToScriptStartOffset), out var autoSkipToScriptStartOffset)) AutoSkipToScriptStartOffset = autoSkipToScriptStartOffset;

            if (settings.TryGetValue(nameof(SyncSettings), out var syncSettingsToken)) syncSettingsToken.Populate(SyncSettings);
        }
    }

    public void Handle(SyncRequestMessage message) => ResetSync(true, message.Axes);
    public void Handle(ReloadScriptsRequestMessage message) => ReloadAxes(message.Axes);

    public void Handle(ChangeScriptMessage message)
    {
        if (message.Scripts == null || message.Scripts.Count == 0)
            return;

        Logger.Info("Received {name:l} [Axes: {list}]", nameof(ChangeScriptMessage), message.Scripts.Keys);
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
                state.Index = AxisState.InvalidIndex;
        }
    }

    private double GetAxisPosition(DeviceAxis axis) => MediaPosition - GlobalOffset - AxisSettings[axis].Offset;
    public double GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

    public void BeginSnapshotPolling(object context)
    {
        foreach (var (_, state) in AxisStates)
            state.ScriptSnapshotEvent.RegisterContext(context);
    }

    public void EndSnapshotPolling(object context)
    {
        foreach (var (_, state) in AxisStates)
            state.ScriptSnapshotEvent.UnregisterContext(context);
    }

    public (DeviceAxis, DeviceAxisScriptSnapshot) WaitForSnapshotAny(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken)
    {
        axes ??= DeviceAxis.All;
        var (index, snapshot) = BroadcastEvent<DeviceAxisScriptSnapshot>.WaitAny(axes.Select(a => AxisStates[a].ScriptSnapshotEvent).ToArray(), context, cancellationToken);
        return (axes[index], snapshot);
    }

    public async ValueTask<(DeviceAxis, DeviceAxisScriptSnapshot)> WaitForSnapshotAnyAsync(IReadOnlyList<DeviceAxis> axes, object context, CancellationToken cancellationToken)
    {
        axes ??= DeviceAxis.All;
        var (index, snapshot) = await BroadcastEvent<DeviceAxisScriptSnapshot>.WaitAnyAsync(axes.Select(a => AxisStates[a].ScriptSnapshotEvent).ToArray(), context, cancellationToken);
        return (axes[index], snapshot);
    }

    public (bool, DeviceAxisScriptSnapshot) WaitForSnapshot(DeviceAxis axis, object context, CancellationToken cancellationToken)
        => AxisStates[axis].ScriptSnapshotEvent.WaitOne(context, cancellationToken);

    public async ValueTask<(bool, DeviceAxisScriptSnapshot)> WaitForSnapshotAsync(DeviceAxis axis, object context, CancellationToken cancellationToken)
        => await AxisStates[axis].ScriptSnapshotEvent.WaitOneAsync(context, cancellationToken);

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
            if (model.Settings.LinkAxisHasPriority || model.Script == null || model.Script is LinkedScriptResource)
            {
                if (model.Settings.LinkAxis == null)
                {
                    ResetAxes(axis);
                }
                else
                {
                    Logger.Debug("Linked {0} to {1}", axis, model.Settings.LinkAxis);
                    SetScript(axis, ScriptResource.LinkTo(AxisModels[model.Settings.LinkAxis].Script));
                }
            }
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
            state.Index = AxisState.InvalidIndex;
            model.Script = script;
        }

        if (script != null)
            Logger.Info("Set {0} script to \"{1}\" from \"{2}\"", axis, script.Name, script.Source);

        _eventAggregator.Publish(new ScriptChangedMessage(axis, script));

        UpdateLinkedScriptsTo(axis);
    }

    private void SetScriptFromReader(ScriptReaderResult result, DeviceAxis desiredAxis)
    {
        if (!result.IsSuccess)
            return;

        if (result.IsMultiAxis)
        {
            ResetSync(true, result.Resources.Keys);
            foreach (var (resourceAxis, resource) in result.Resources)
                SetScript(resourceAxis, resource);
        }
        else
        {
            ResetSync(true, desiredAxis);
            SetScript(desiredAxis, result.Resource);
        }
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

        ScriptRepositoryManager.BeginSearchForScripts(MediaResource, axesWithoutLinkPriority, scripts =>
            {
                UpdateLinkScriptFor(axesWithoutLinkPriority.Except(scripts.Keys));
                foreach (var (axis, resource) in scripts)
                    SetScript(axis, resource);

                UpdateLinkScriptFor(axesWithLinkPriority);
                _eventAggregator.Publish(new PostScriptSearchMessage(scripts));
            }, _cancellationSource.Token);
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

        if (MediaResource.IsUrl)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MediaResource.Path,
                UseShellExecute = true
            });
        }
        else
        {
            var directory = Path.GetDirectoryName(MediaResource.Path);
            if (Directory.Exists(directory))
                Process.Start("explorer.exe", directory);
        }
    }

    public void OnPlayPauseClick()
    {
        _eventAggregator.Publish(new MediaPlayPauseMessage(!IsPlaying));
    }

    public void OnSeekRequest(object sender, SeekRequestEventArgs e) => SeekMediaToTime(e.Position.TotalSeconds);

    private void InvalidateMediaState()
    {
        MediaResource = null;
        MediaDuration = double.NaN;
        PlaybackSpeed = 1;
        SetMediaPositionInternal(double.NaN);
        ClearMediaLoop();
    }

    private void SetMediaPositionInternal(double position)
    {
        MediaPosition = position;
        _internalMediaPosition = position;

        NotifyOfPropertyChange(nameof(MediaPosition));
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

        _eventAggregator.Publish(new MediaSeekMessage(TimeSpan.FromSeconds(Math.Clamp(time, 0, MediaDuration))));
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

    public void ClearMediaLoop() => MediaLoopSegment.Clear();
    public void SetMediaLoopStart(double position)
    {
        MediaLoopSegment.StartPosition = position;
        if (MediaLoopSegment.IsValid)
            SeekMediaToTime(MediaLoopSegment.StartPosition.Value);
    }

    public void SetMediaLoopEnd(double position)
    {
        MediaLoopSegment.EndPosition = position;
        if (MediaLoopSegment.IsValid)
            SeekMediaToTime(MediaLoopSegment.StartPosition.Value);
    }

    public void SetMediaLoop(double startPosition, double endPosition)
    {
        ClearMediaLoop();
        SetMediaLoopStart(startPosition);
        SetMediaLoopEnd(endPosition);
    }

    public void SetMediaLoopStartFromMediaPosition() => SetMediaLoopStart(MediaPosition);
    public void SetMediaLoopEndFromMediaPosition() => SetMediaLoopEnd(MediaPosition);
    public void SetMediaLoopFromCurrentChapter()
    {
        var (_, chapters) = AxisChapters.FirstOrDefault(x => x.Value != null);
        if (chapters == null)
            return;

        if (!chapters.TryFindIntersecting(MediaPosition, 1, out var chapter))
            return;

        SetMediaLoop(chapter.StartPosition, chapter.EndPosition);
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
            case nameof(ViewModels.AxisSettings.MotionProviderFillGaps):
            case nameof(ViewModels.AxisSettings.BypassScript):
            case nameof(ViewModels.AxisSettings.BypassMotionProvider):
            case nameof(ViewModels.AxisSettings.BypassTransition):
            case nameof(ViewModels.AxisSettings.AutoHomeEnabled):
            case nameof(ViewModels.AxisSettings.AutoHomeTargetValue):
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
        if (drop is not IEnumerable<string> paths)
            return;

        var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
        if (path == null)
            return;

        SetScriptFromReader(FunscriptReader.Default.FromPath(path), axis);
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
        var dialog = new OpenFileDialog()
        {
            CheckFileExists = true,
            CheckPathExists = true,
            InitialDirectory = Directory.Exists(MediaResource?.Source) ? MediaResource.Source : string.Empty,
            Filter = "Funscript files (*.funscript)|*.funscript"
        };

        if (dialog.ShowDialog() != true)
            return;

        SetScriptFromReader(FunscriptReader.Default.FromPath(dialog.FileName), axis);
    }

    public void OnAxisClear(DeviceAxis axis) => ResetAxes(axis);
    public void OnAxisReload(DeviceAxis axis) => ReloadAxes(axis);

    public void SetAxisTransition(DeviceAxis axis, double value, double duration, bool offset = false)
    {
        if (axis == null)
            return;

        var state = AxisStates[axis];
        lock (state)
        {
            var fromValue = double.IsFinite(state.Value) ? state.Value : axis.DefaultValue;
            var toValue = MathUtils.Clamp01(offset ? fromValue + value : value);
            state.ExternalTransition.Reset(fromValue, toValue, duration);
        }
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

    public void OnBypassAll(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, _) = pair;
        var settings = AxisSettings[axis];
        settings.BypassScript = true;
        settings.BypassMotionProvider = true;
        settings.BypassTransition = true;
    }

    [SuppressPropertyChangedWarnings]
    public void OnInvertValueCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
            return;

        var (axis, _) = pair;
        var settings = AxisSettings[axis];

        ResetSync(true, axis);
        settings.InvertValue = button.IsChecked ?? false;
    }
    #endregion

    #region MediaResource
    public async void OnMediaPathModifierConfigure(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        _ = await DialogHost.Show(modifier, "MediaPathModifierDialog");

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

        var dialog = new OpenFileDialog()
        {
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        MediaPathModifiers.Add(new FindReplaceMediaPathModifier()
        {
            Find = MediaResource.OriginalPath,
            Replace = dialog.FileName
        });

        Handle(new MediaPathChangedMessage(MediaResource.OriginalPath));
    }
    #endregion

    #region Shortcuts
    public void RegisterActions(IShortcutManager s)
    {
        void UpdateSettings(DeviceAxis axis, Action<AxisSettings> callback)
        {
            if (axis != null)
                callback(AxisSettings[axis]);
        }

        #region Media::PlayPause
        s.RegisterAction<bool>("Media::PlayPause::Set",
            s => s.WithLabel("Play"), play =>
            {
                if (play && !IsPlaying) OnPlayPauseClick();
                else if (!play && IsPlaying) OnPlayPauseClick();
            });

        s.RegisterAction("Media::PlayPause::Toggle", () => OnPlayPauseClick());
        #endregion

        #region Media::Path
        s.RegisterAction<string>("Media::Path::Set", s => s.WithLabel("Media path"), path => _eventAggregator.Publish(new MediaChangePathMessage(path)));
        #endregion

        #region Media::Speed
        s.RegisterAction<double>("Media::Speed::Offset",
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"), offset => _eventAggregator.Publish(new MediaChangeSpeedMessage(CoerceMediaSpeed(PlaybackSpeed + offset / 100))));
        s.RegisterAction<double>("Media::Speed::Set",
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"), value => _eventAggregator.Publish(new MediaChangeSpeedMessage(CoerceMediaSpeed(value / 100))));

        static double CoerceMediaSpeed(double speed)
        {
            if (Math.Abs(1 - speed) < 0.01)
                speed = 1;
            return Math.Max(speed, 0.01);
        }
        #endregion

        #region Media::ScriptOffset
        s.RegisterAction<double>("Media::ScriptOffset::Offset",
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}s"), offset => GlobalOffset += offset);
        s.RegisterAction<double>("Media::ScriptOffset::Set",
            s => s.WithLabel("Value").WithStringFormat("{}{0}s"), value => GlobalOffset = value);
        #endregion

        #region Media::Position
        s.RegisterAction<double>("Media::Position::Time::Offset",
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}s"), offset => SeekMediaToTime(MediaPosition + offset));
        s.RegisterAction<double>("Media::Position::Time::Set",
            s => s.WithLabel("Value").WithStringFormat("{}{0}s"), value => SeekMediaToTime(value));

        s.RegisterAction<double>("Media::Position::Percent::Offset",
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            offset => SeekMediaToPercent(MediaPosition / MediaDuration + offset / 100));
        s.RegisterAction<double>("Media::Position::Percent::Set",
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            value => SeekMediaToPercent(value / 100));

        s.RegisterAction<double>("Media::Position::SkipToScriptStart",
            s => s.WithLabel("Offset").WithStringFormat("{}{0}s"), offset => SeekMediaToScriptStart(offset, onlyWhenBefore: false));
        #endregion

        #region Media::Loop
        s.RegisterAction<double, double>("Media::Loop::Set::FromMediaPositionOffset",
            s => s.WithLabel("Start offset").WithStringFormat("{}{0}s").WithDescription("Seconds before media position"),
            s => s.WithLabel("End offset").WithStringFormat("{}{0}s").WithDescription("Seconds after media position"),
            (startOffset, endOffset) => SetMediaLoop(MediaPosition - startOffset, MediaPosition + endOffset));

        s.RegisterAction<double, double>("Media::Loop::Set",
            s => s.WithLabel("Start position").WithStringFormat("{}{0}s"),
            s => s.WithLabel("End position").WithStringFormat("{}{0}s"),
            (startPosition, endPosition) => SetMediaLoop(startPosition, endPosition));

        s.RegisterAction("Media::Loop::CycleSetStartEnd", () =>
            {
                if (MediaLoopSegment.IsValid)
                    ClearMediaLoop();
                else if (MediaLoopSegment.StartPosition == null)
                    SetMediaLoopStartFromMediaPosition();
                else if (MediaLoopSegment.EndPosition == null)
                    SetMediaLoopEndFromMediaPosition();
            });

        s.RegisterAction<double>("Media::Loop::Start::Set", s => s.WithLabel("Position").WithStringFormat("{}{0}s"), position => SetMediaLoopStart(position));
        s.RegisterAction<double>("Media::Loop::End::Set", s => s.WithLabel("Position").WithStringFormat("{}{0}s"), position => SetMediaLoopEnd(position));
        s.RegisterAction("Media::Loop::Clear", () => ClearMediaLoop());
        s.RegisterAction("Media::Loop::Set::FromCurrentChapter", () => SetMediaLoopFromCurrentChapter());
        s.RegisterAction("Media::Loop::Start::Set::FromMediaPosition", () => SetMediaLoopStartFromMediaPosition());
        s.RegisterAction("Media::Loop::End::Set::FromMediaPosition", () => SetMediaLoopEndFromMediaPosition());
        #endregion

        #region Media::AutoSkipToScriptStartEnabled
        s.RegisterAction<bool>("Media::AutoSkipToScriptStartEnabled::Set",
            s => s.WithLabel("Auto-skip to script start enabled"), enabled => AutoSkipToScriptStartEnabled = enabled);

        s.RegisterAction("Media::AutoSkipToScriptStartEnabled::Toggle",
            () => AutoSkipToScriptStartEnabled = !AutoSkipToScriptStartEnabled);
        #endregion

        #region Media::AutoSkipToScriptStartOffset
        s.RegisterAction<double>("Media::AutoSkipToScriptStartOffset::Set",
            s => s.WithLabel("Script start auto-skip offset").WithStringFormat("{}{0}s"), offset => AutoSkipToScriptStartOffset = offset);
        #endregion

        #region Media::Bookmark
        bool TryGetFirstBookmarks(out BookmarkCollection bookmarks)
        {
            (_, bookmarks) = AxisBookmarks.FirstOrDefault(x => x.Value != null);
            return bookmarks != null;
        }

        s.RegisterAction<string>("Media::Bookmark::SeekToByName",
            s => s.WithLabel("Bookmark name"), name =>
            {
                if (TryGetFirstBookmarks(out var bookmarks) && bookmarks.TryFindByName(name, out var bookmark))
                    SeekMediaToTime(bookmark.Position);
            });

        s.RegisterAction<int>("Media::Bookmark::SeekToByIndex",
            s => s.WithLabel("Bookmark index"), index =>
            {
                if (TryGetFirstBookmarks(out var bookmarks) && bookmarks.ValidateIndex(index))
                    SeekMediaToTime(bookmarks[index].Position);
            });

        s.RegisterAction("Media::Bookmark::SeekToNext", () =>
            {
                if (!TryGetFirstBookmarks(out var bookmarks))
                    return;

                var index = bookmarks.SearchForIndexAfter(MediaPosition + 1.5);
                if (bookmarks.ValidateIndex(index))
                    SeekMediaToTime(bookmarks[index].Position);
            });

        s.RegisterAction("Media::Bookmark::SeekToPrev", () =>
        {
            if (!TryGetFirstBookmarks(out var bookmarks))
                return;

            var index = bookmarks.SearchForIndexBefore(MediaPosition - 1.5);
            if (bookmarks.ValidateIndex(index))
                SeekMediaToTime(bookmarks[index].Position);
        });
        #endregion

        #region Media::Chapter
        bool TryGetFirstChapters(out ChapterCollection chapters)
        {
            (_, chapters) = AxisChapters.FirstOrDefault(x => x.Value != null);
            return chapters != null;
        }

        s.RegisterAction<string>("Media::Chapter::SeekToByName",
            s => s.WithLabel("Chapter name"), name =>
            {
                if (TryGetFirstChapters(out var chapters) && chapters.TryFindByName(name, out var chapter))
                    SeekMediaToTime(chapter.StartPosition);
            });

        s.RegisterAction<int>("Media::Chapter::SeekToByIndex",
            s => s.WithLabel("Chapter index"), index =>
            {
                if (TryGetFirstChapters(out var chapters) && chapters.ValidateIndex(index))
                    SeekMediaToTime(chapters[index].StartPosition);
            });

        s.RegisterAction("Media::Chapter::SeekToNext", () =>
        {
            if (!TryGetFirstChapters(out var chapters))
                return;

            var index = chapters.SearchForIndexAfter(MediaPosition + 1.5);
            if (chapters.ValidateIndex(index))
                SeekMediaToTime(chapters[index].StartPosition);
        });

        s.RegisterAction("Media::Chapter::SeekToPrev", () =>
        {
            if (!TryGetFirstChapters(out var chapters))
                return;

            var index = chapters.SearchForIndexBefore(MediaPosition - 1.5);
            if (chapters.ValidateIndex(index))
                SeekMediaToTime(chapters[index].StartPosition);
        });
        #endregion

        #region Script::SkipGap
        s.RegisterAction<DeviceAxis, double>("Script::SkipGap",
            s => s.WithLabel("Target").WithItemsSource(DeviceAxis.All).WithDescription("Target axis script to check for gaps\nEmpty to check all scripts"),
            s => s.WithLabel("Minimum skip").WithDefaultValue(2).WithStringFormat("{}{0}s"),
            (axis, minimumSkip) => SkipGap(minimumSkip, axis));
        #endregion

        #region Axis::Value
        s.RegisterAction<DeviceAxis, double, double>("Axis::Value::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:P0}"),
            s => s.WithLabel("Duration").WithStringFormat("{}{0:0.00}s"),
            (axis, offset, duration) => SetAxisTransition(axis, offset, duration, offset: true));

        s.RegisterAction<DeviceAxis, double, double>("Axis::Value::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:P0}"),
            s => s.WithLabel("Duration").WithStringFormat("{}{0:0.00}s"),
            (axis, value, duration) => SetAxisTransition(axis, value, duration));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis>("Axis::Value::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (data, axis) => SetAxisTransition(axis, data.ValueOrDelta, data.DeltaTime, offset: data.IsRelative));
        #endregion

        #region Axis::Sync
        s.RegisterAction<DeviceAxis>("Axis::Sync",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => { if (axis != null) ResetSync(true, axis); });

        s.RegisterAction("Axis::SyncAll", () => ResetSync(true, null));
        #endregion

        #region Axis::Bypass::All
        s.RegisterAction<DeviceAxis, bool>("Axis::Bypass::All::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Bypass all"),
            (axis, enabled) =>
            {
                if (axis == null)
                    return;

                var settings = AxisSettings[axis];
                settings.BypassScript = enabled;
                settings.BypassMotionProvider = enabled;
                settings.BypassTransition = enabled;
            });

        s.RegisterAction<DeviceAxis>("Axis::Bypass::All::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis =>
            {
                if (axis == null)
                    return;

                var settings = AxisSettings[axis];
                var state = settings.BypassScript || settings.BypassMotionProvider || settings.BypassTransition;
                settings.BypassScript = !state;
                settings.BypassMotionProvider = !state;
                settings.BypassTransition = !state;
            });
        #endregion

        #region Axis::Bypass::Script
        s.RegisterAction<DeviceAxis, bool>("Axis::Bypass::Script::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Bypass script"),
            (axis, enabled) => UpdateSettings(axis, s => s.BypassScript = enabled));

        s.RegisterAction<DeviceAxis>("Axis::Bypass::Script::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.BypassScript = !s.BypassScript));
        #endregion

        #region Axis::Bypass::MotionProvider
        s.RegisterAction<DeviceAxis, bool>("Axis::Bypass::MotionProvider::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Bypass motion provider"),
            (axis, enabled) => UpdateSettings(axis, s => s.BypassMotionProvider = enabled));

        s.RegisterAction<DeviceAxis>("Axis::Bypass::MotionProvider::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.BypassMotionProvider = !s.BypassMotionProvider));
        #endregion

        #region Axis::Bypass::Transition
        s.RegisterAction<DeviceAxis, bool>("Axis::Bypass::Transition::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Bypass custom transition"),
            (axis, enabled) => UpdateSettings(axis, s => s.BypassTransition = enabled));

        s.RegisterAction<DeviceAxis>("Axis::Bypass::Transition::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.BypassTransition = !s.BypassTransition));
        #endregion

        #region Axis::ClearScript
        s.RegisterAction<DeviceAxis>("Axis::ClearScript",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => { if (axis != null) OnAxisClear(axis); });
        #endregion

        #region Axis::ReloadScript
        s.RegisterAction<DeviceAxis>("Axis::ReloadScript",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => { if (axis != null) OnAxisReload(axis); });
        #endregion

        #region Axis::InterpolationType
        s.RegisterAction<DeviceAxis, InterpolationType>("Axis::InterpolationType::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Interpolation").WithItemsSource(Enum.GetValues<InterpolationType>()),
            (axis, type) => UpdateSettings(axis, s => s.InterpolationType = type));
        #endregion

        #region Axis::InvertValue
        s.RegisterAction<DeviceAxis, bool>("Axis::InvertValue::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Invert script"),
            (axis, enabled) => UpdateSettings(axis, s => s.InvertValue = enabled));

        s.RegisterAction<DeviceAxis>("Axis::InvertValue::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.InvertValue = !s.InvertValue));
        #endregion

        #region Axis::LinkPriority
        s.RegisterAction<DeviceAxis, bool>("Axis::LinkPriority::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Link has priority").WithDescription("When enabled the link has priority\nover automatically loaded scripts"),
            (axis, enabled) => UpdateSettings(axis, s => s.LinkAxisHasPriority = enabled));

        s.RegisterAction<DeviceAxis>("Axis::LinkPriority::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.LinkAxisHasPriority = !s.LinkAxisHasPriority));
        #endregion

        #region Axis::SmartLimitInputAxis
        s.RegisterAction<DeviceAxis, DeviceAxis>("Axis::SmartLimitInputAxis::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Input axis").WithItemsSource(DeviceAxis.All),
            (source, input) =>
            {
                if (source == null || source == input)
                    return;

                ResetSync(true, source);
                AxisSettings[source].SmartLimitInputAxis = input;
            });
        #endregion

        #region Axis::SmartLimitMode
        s.RegisterAction<DeviceAxis, SmartLimitMode>("Axis::SmartLimitMode::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Smart limit mode").WithItemsSource(Enum.GetValues<SmartLimitMode>()),
            (axis, mode) => UpdateSettings(axis, s => s.SmartLimitMode = mode));
        #endregion

        #region Axis::SmartLimitTargetValue
        s.RegisterAction<DeviceAxis, double>("Axis::SmartLimitTargetValue::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:P0}"),
            (axis, offset) => UpdateSettings(axis, s => s.SmartLimitTargetValue = MathUtils.Clamp01(s.SmartLimitTargetValue + offset)));

        s.RegisterAction<DeviceAxis, double>("Axis::SmartLimitTargetValue::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:P0}"),
            (axis, value) => UpdateSettings(axis, s => s.SmartLimitTargetValue = MathUtils.Clamp01(value)));
        #endregion

        #region Axis::SmartLimitPoints
        s.RegisterAction<DeviceAxis, ObservableConcurrentCollection<Point>>("Axis::SmartLimitPoints::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithDefaultValue([new(50, 50)])
                  .WithTemplateName("SmartLimitPointsTemplate")
                  .WithCustomToString(x => $"Points({x.Count})"),
            (axis, points) => UpdateSettings(axis, s => s.SmartLimitPoints.SetFrom(points)));
        #endregion

        #region Axis::LinkAxis
        s.RegisterAction<DeviceAxis, DeviceAxis>("Axis::LinkAxis::Set",
            s => s.WithLabel("Source axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (source, target) =>
            {
                if (source == null || source == target)
                    return;

                AxisSettings[source].LinkAxis = target;
                ReloadAxes(source);
            });
        #endregion

        #region Axis::SpeedLimitEnabled
        s.RegisterAction<DeviceAxis, bool>("Axis::SpeedLimitEnabled::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Speed limit enabled"),
            (axis, enabled) => UpdateSettings(axis, s => s.SpeedLimitEnabled = enabled));

        s.RegisterAction<DeviceAxis>("Axis::SpeedLimitEnabled::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.SpeedLimitEnabled = !s.SpeedLimitEnabled));
        #endregion

        #region Axis::SpeedLimitSecondsPerStroke
        s.RegisterAction<DeviceAxis, double>("Axis::SpeedLimitSecondsPerStroke::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:F3}s/stroke"),
            (axis, offset) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = Math.Clamp(s.MaximumSecondsPerStroke + offset, 0.001, 10)));

        s.RegisterAction<DeviceAxis, double>("Axis::SpeedLimitSecondsPerStroke::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:F3}s/stroke"),
            (axis, value) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = Math.Clamp(value, 0.001, 10)));
        #endregion

        #region Axis::AutoHomeEnabled
        s.RegisterAction<DeviceAxis, bool>("Axis::AutoHomeEnabled::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Auto home enabled"),
            (axis, enabled) => UpdateSettings(axis, s => s.AutoHomeEnabled = enabled));

        s.RegisterAction<DeviceAxis>("Axis::AutoHomeEnabled::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.AutoHomeEnabled = !s.AutoHomeEnabled));
        #endregion

        #region Axis::AutoHomeDelay
        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeDelay::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}s"),
            (axis, offset) => UpdateSettings(axis, s => s.AutoHomeDelay = Math.Max(0, s.AutoHomeDelay + offset)));

        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeDelay::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}s"),
            (axis, value) => UpdateSettings(axis, s => s.AutoHomeDelay = Math.Max(0, value)));
        #endregion

        #region Axis::AutoHomeInsideScript
        s.RegisterAction<DeviceAxis, bool>("Axis::AutoHomeInsideScript::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Auto home inside script"),
            (axis, enabled) => UpdateSettings(axis, s => s.AutoHomeInsideScript = enabled));

        s.RegisterAction<DeviceAxis>("Axis::AutoHomeInsideScript::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.AutoHomeInsideScript = !s.AutoHomeInsideScript));
        #endregion

        #region Axis::AutoHomeDuration
        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeDuration::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}s"),
            (axis, offset) => UpdateSettings(axis, s => s.AutoHomeDuration = Math.Max(0, s.AutoHomeDuration + offset)));

        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeDuration::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}s"),
            (axis, value) => UpdateSettings(axis, s => s.AutoHomeDuration = Math.Max(0, value)));
        #endregion

        #region Axis::AutoHomeTargetValue
        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeTargetValue::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:P0}"),
            (axis, offset) => UpdateSettings(axis, s => s.AutoHomeTargetValue = MathUtils.Clamp01(s.AutoHomeTargetValue + offset)));

        s.RegisterAction<DeviceAxis, double>("Axis::AutoHomeTargetValue::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:P0}"),
            (axis, value) => UpdateSettings(axis, s => s.AutoHomeTargetValue = MathUtils.Clamp01(value)));
        #endregion

        #region Axis::ScriptOffset
        s.RegisterAction<DeviceAxis, double>("Axis::ScriptOffset::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:0.00}s"),
            (axis, offset) => UpdateSettings(axis, s => s.Offset += offset));

        s.RegisterAction<DeviceAxis, double>("Axis::ScriptOffset::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:0.00}s"),
            (axis, value) => UpdateSettings(axis, s => s.Offset = value));
        #endregion

        #region Axis::ScriptScale
        s.RegisterAction<DeviceAxis, double>("Axis::ScriptScale::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            (axis, offset) => UpdateSettings(axis, s => s.ScriptScale = Math.Clamp(s.ScriptScale + offset, 1, 400)));

        s.RegisterAction<DeviceAxis, double>("Axis::ScriptScale::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => s.ScriptScale = Math.Clamp(value, 1, 400)));
        #endregion

        #region Axis::MotionProvider
        var motionProviderNames = MotionProviderManager.MotionProviderNames;
        s.RegisterAction<DeviceAxis, string>("Axis::MotionProvider::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Motion provider").WithItemsSource(motionProviderNames),
            (axis, motionProviderName) => UpdateSettings(axis, s => s.SelectedMotionProvider = motionProviderNames.Contains(motionProviderName) ? motionProviderName : null));

        MotionProviderManager.RegisterActions(s);
        #endregion

        #region Axis::MotionProviderBlend
        s.RegisterAction<DeviceAxis, double>("Axis::MotionProviderBlend::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset"),
            (axis, offset) => UpdateSettings(axis, s => s.MotionProviderBlend = Math.Clamp(s.MotionProviderBlend + offset, 0, 100)));

        s.RegisterAction<DeviceAxis, double>("Axis::MotionProviderBlend::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
            (axis, value) => UpdateSettings(axis, s => s.MotionProviderBlend = Math.Clamp(value, 0, 100)));

        s.RegisterAction<IAxisInputGestureData, DeviceAxis>("Axis::MotionProviderBlend::Drive",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            (data, axis) => UpdateSettings(axis, s => s.MotionProviderBlend = Math.Clamp(data.ApplyTo(s.MotionProviderBlend, 100), 0, 100)));
        #endregion

        #region Axis::MotionProviderFillGaps
        s.RegisterAction<DeviceAxis, bool>("Axis::MotionProviderFillGaps::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Fill gaps"),
            (axis, enabled) => UpdateSettings(axis, s => s.MotionProviderFillGaps = enabled));

        s.RegisterAction<DeviceAxis>("Axis::MotionProviderFillGaps::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.MotionProviderFillGaps = !s.MotionProviderFillGaps));
        #endregion

        #region Axis::MotionProviderMinimumGapDuration
        s.RegisterAction<DeviceAxis, double>("Axis::MotionProviderMinimumGapDuration::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0:0.00}s"),
            (axis, offset) => UpdateSettings(axis, s => s.MotionProviderMinimumGapDuration = Math.Max(0, s.MotionProviderMinimumGapDuration + offset)));

        s.RegisterAction<DeviceAxis, double>("Axis::MotionProviderMinimumGapDuration::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value").WithStringFormat("{}{0:0.00}s"),
            (axis, value) => UpdateSettings(axis, s => s.MotionProviderMinimumGapDuration = Math.Max(0, value)));
        #endregion

        #region Axis::UpdateMotionProviderWithAxis
        s.RegisterAction<DeviceAxis, DeviceAxis>("Axis::UpdateMotionProviderWithAxis::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Input axis").WithItemsSource(DeviceAxis.All),
            (source, input) =>
            {
                if (source == null || source == input)
                    return;

                ResetSync(true, source);
                AxisSettings[source].UpdateMotionProviderWithAxis = input;
            });
        #endregion

        #region Axis::UpdateMotionProviderWhenPaused
        s.RegisterAction<DeviceAxis, bool>("Axis::UpdateMotionProviderWhenPaused::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Update motion providers when paused"),
            (axis, enabled) => UpdateSettings(axis, s => s.UpdateMotionProviderWhenPaused = enabled));

        s.RegisterAction<DeviceAxis>("Axis::UpdateMotionProviderWhenPaused::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.UpdateMotionProviderWhenPaused = !s.UpdateMotionProviderWhenPaused));
        #endregion

        #region Axis::UpdateMotionProviderWithoutScript
        s.RegisterAction<DeviceAxis, bool>("Axis::UpdateMotionProviderWithoutScript::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Update motion providers without script"),
            (axis, enabled) => UpdateSettings(axis, s => s.UpdateMotionProviderWithoutScript = enabled));

        s.RegisterAction<DeviceAxis>("Axis::UpdateMotionProviderWithoutScript::Toggle",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All), axis => UpdateSettings(axis, s => s.UpdateMotionProviderWithoutScript = !s.UpdateMotionProviderWithoutScript));
        #endregion
    }
    #endregion

    #region Properties 
    private void RegisterProperties(IPropertyManager p)
    {
        p.RegisterProperty<DeviceAxis, IScriptResource>("Axis::Script", axis => AxisModels[axis].Script);
        p.RegisterProperty<DeviceAxis, double>("Axis::Position", GetAxisPosition);

        p.RegisterProperty<DeviceAxis, int>("Axis::Index", axis => AxisStates[axis].Index);
        p.RegisterProperty<DeviceAxis, double>("Axis::Value", axis => AxisStates[axis].Value);
        p.RegisterProperty<DeviceAxis, double>("Axis::ScriptValue", axis => AxisStates[axis].ScriptValue);
        p.RegisterProperty<DeviceAxis, double>("Axis::TransitionValue", axis => AxisStates[axis].TransitionValue);
        p.RegisterProperty<DeviceAxis, double>("Axis::MotionProviderValue", axis => AxisStates[axis].MotionProviderValue);
        p.RegisterProperty<DeviceAxis, double>("Axis::Speed", axis => AxisStates[axis].Speed);

        p.RegisterProperty<DeviceAxis, InterpolationType>("Axis::InterpolationType", axis => AxisSettings[axis].InterpolationType);
        p.RegisterProperty<DeviceAxis, double>("Axis::Offset", axis => AxisSettings[axis].Offset);
        p.RegisterProperty<DeviceAxis, double>("Axis::ScriptScale", axis => AxisSettings[axis].ScriptScale);
        p.RegisterProperty<DeviceAxis, bool>("Axis::BypassScript", axis => AxisSettings[axis].BypassScript);
        p.RegisterProperty<DeviceAxis, bool>("Axis::BypassMotionProvider", axis => AxisSettings[axis].BypassMotionProvider);
        p.RegisterProperty<DeviceAxis, bool>("Axis::BypassTransition", axis => AxisSettings[axis].BypassTransition);

        p.RegisterProperty("Media::IsPlaying", () => IsPlaying);
        p.RegisterProperty("Media::Speed", () => PlaybackSpeed);
        p.RegisterProperty("Media::Duration", () => MediaDuration);
        p.RegisterProperty("Media::Position", () => MediaPosition);
    }
    #endregion

    private void Dispose(bool disposing)
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

internal sealed class AxisModel(DeviceAxis axis) : PropertyChangedBase
{
    public AxisState State { get; } = new AxisState();
    public AxisSettings Settings { get; } = new AxisSettings(axis);
    public IScriptResource Script { get; set; } = null;
}

[AddINotifyPropertyChangedInterface]
internal sealed partial class AxisState
{
    public static int AfterScriptIndex { get; } = int.MaxValue;
    public static int BeforeScriptIndex { get; } = -1;
    public static int InvalidIndex { get; } = int.MinValue;

    [DoNotNotify] public double Value { get; set; } = double.NaN;
    [DoNotNotify] public double ScriptValue { get; set; } = double.NaN;
    [DoNotNotify] public double TransitionValue { get; set; } = double.NaN;
    [DoNotNotify] public double MotionProviderValue { get; set; } = double.NaN;
    [DoNotNotify] public double Speed { get; set; } = double.NaN;

    [DoNotNotify] public AxisValueTransition ExternalTransition { get; } = new AxisValueTransition();
    [DoNotNotify] public BroadcastEvent<DeviceAxisScriptSnapshot> ScriptSnapshotEvent { get; } = new BroadcastEvent<DeviceAxisScriptSnapshot>();

    [DoNotNotify] public int Index { get; set; } = InvalidIndex;
    [DoNotNotify] public bool Invalid => Index == InvalidIndex;
    [DoNotNotify] public bool BeforeScript => Index == BeforeScriptIndex;
    [DoNotNotify] public bool AfterScript => Index == AfterScriptIndex;
    [DoNotNotify] public bool InsideScript => Index >= 0 && Index != AfterScriptIndex;

    [DoNotNotify] public bool InsideGap { get; set; } = false;

    [DoNotNotify] public double SyncTime { get; set; } = 0;
    [DoNotNotify] public double AutoHomeTime { get; set; } = 0;

    [DoNotNotify] public bool IsDirty { get; set; } = false;
    [DoNotNotify] public bool IsAutoHoming { get; set; } = false;

    public bool IsSpeedLimited { get; set; } = false;
    public bool IsSmartLimited { get; set; } = false;

    public void NotifyValueChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InsideScript)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}

internal sealed class AxisValueTransition
{
    private bool _initialized;
    private double _fromValue;
    private double _toValue;
    private double _duration;
    private double _time;

    public AxisValueTransition() => _initialized = false;

    public bool Update(double deltaTime)
    {
        if (!_initialized)
            return false;
        if (_time >= 0 && _time >= _duration)
            return false;

        _time = _time < 0 ? deltaTime : _time + deltaTime;
        return true;
    }

    public double GetValue()
    {
        if (_duration < 0.00001)
            return _toValue;

        var t = MathUtils.Clamp01(_time / _duration);
        return MathUtils.Lerp(_fromValue, _toValue, t);
    }

    public void Reset(double fromValue, double toValue, double duration)
    {
        _initialized = true;
        _fromValue = fromValue;
        _toValue = toValue;
        _duration = duration;
        _time = -1;
    }
}

internal sealed class AxisStateUpdateContext(AxisModel model)
{
    private readonly AxisModel _model = model;
    private readonly AxisState _state = model.State;

    public int Index { get; set; }
    public bool Invalid => Index == AxisState.InvalidIndex;
    public bool BeforeScript => Index == AxisState.BeforeScriptIndex;
    public bool AfterScript => Index == AxisState.AfterScriptIndex;
    public bool InsideScript => Index >= 0 && Index != AxisState.AfterScriptIndex;

    public int LastIndex => _state.Index;
    public bool LastInvalid => _state.Invalid;
    public bool LastBeforeScript => _state.BeforeScript;
    public bool LastAfterScript => _state.AfterScript;
    public bool LastInsideScript => _state.InsideScript;

    public double Value { get; set; }
    public double ScriptValue { get; set; }
    public double TransitionValue { get; set; }
    public double MotionProviderValue { get; set; }

    public double LastValue => _state.Value;
    public double LastScriptValue => _state.ScriptValue;
    public double LastMotionProviderValue => _state.MotionProviderValue;
    public double LastTransitionValue => _state.TransitionValue;

    public bool InsideGap { get; set; }
    public bool IsAutoHoming { get; set; }
    public bool IsSpeedLimited { get; set; }
    public bool IsSmartLimited { get; set; }

    public bool LastInsideGap => _state.InsideGap;
    public bool LastIsAutoHoming => _state.IsAutoHoming;
    public bool LastIsSpeedLimited => _state.IsSpeedLimited;
    public bool LastIsSmartLimited => _state.IsSmartLimited;

    public bool IsDirty => ValueChanged(LastValue, Value, 0.000001);
    public bool IsScriptDirty => ValueChanged(LastScriptValue, ScriptValue, 0.000001);
    public bool IsMotionProviderDirty => ValueChanged(LastMotionProviderValue, MotionProviderValue, 0.000001);
    public bool IsTransitionDirty => ValueChanged(LastTransitionValue, TransitionValue, 0.000001);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValueChanged(double last, double current, double epsilon)
        => Math.Abs(last - current) > epsilon || (double.IsFinite(current) ^ double.IsFinite(last));

    public void BeginUpdate()
    {
        Monitor.Enter(_state);

        Value = double.NaN;
        ScriptValue = double.NaN;
        TransitionValue = double.NaN;
        MotionProviderValue = double.NaN;

        Index = _state.Index;
    }

    public void EndUpdate(double deltaTime)
    {
        _state.IsDirty = IsDirty;

        if (double.IsFinite(Value) && double.IsFinite(LastValue))
            _state.Speed = (LastValue - Value) / deltaTime;

        if (LastIndex != Index)
        {
            var keyframes = _model.Script?.Keyframes;
            _state.ScriptSnapshotEvent.Set(new()
            {
                KeyframeFrom = keyframes?.ValidateIndex(Index) == true ? keyframes[Index] : null,
                KeyframeTo = keyframes?.ValidateIndex(Index + 1) == true ? keyframes[Index + 1] : null,
                IndexFrom = Index,
                IndexTo = Index + 1
            });
        }

        _state.Index = Index;
        _state.Value = Value;
        _state.ScriptValue = ScriptValue;
        _state.TransitionValue = TransitionValue;
        _state.MotionProviderValue = MotionProviderValue;

        _state.InsideGap = InsideGap;
        _state.IsAutoHoming = IsAutoHoming;
        _state.IsSpeedLimited = IsSpeedLimited;
        _state.IsSmartLimited = IsSmartLimited;

        Monitor.Exit(_state);
    }
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class AxisSettings : PropertyChangedBase
{
    [JsonProperty] public bool LinkAxisHasPriority { get; set; } = false;
    [JsonProperty] public DeviceAxis LinkAxis { get; set; } = null;

    [JsonProperty] public DeviceAxis SmartLimitInputAxis { get; set; } = null;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableConcurrentCollection<Point> SmartLimitPoints { get; set; } = [new Point(25, 100), new Point(90, 0)];
    [JsonProperty] public SmartLimitMode SmartLimitMode { get; set; } = SmartLimitMode.Value;
    [JsonProperty] public double SmartLimitTargetValue { get; set; }

    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;
    [JsonProperty] public bool AutoHomeEnabled { get; set; } = true;
    [JsonProperty] public double AutoHomeDelay { get; set; } = 5;
    [JsonProperty] public double AutoHomeDuration { get; set; } = 3;
    [JsonProperty] public double AutoHomeTargetValue { get; set; }
    [JsonProperty] public bool AutoHomeInsideScript { get; set; } = false;
    [JsonProperty] public bool InvertValue { get; set; } = false;
    [JsonProperty] public double Offset { get; set; } = 0;
    [JsonProperty] public double ScriptScale { get; set; } = 100;
    [JsonProperty] public bool BypassScript { get; set; } = false;
    [JsonProperty] public bool BypassMotionProvider { get; set; } = false;
    [JsonProperty] public bool BypassTransition { get; set; } = false;
    [JsonProperty] public double MotionProviderBlend { get; set; } = 0;
    [JsonProperty] public bool MotionProviderFillGaps { get; set; } = false;
    [JsonProperty] public double MotionProviderMinimumGapDuration { get; set; } = 5;
    [JsonProperty] public bool MotionProviderSpeedLimitWithAxis { get; set; } = true;
    [JsonProperty] public bool UpdateMotionProviderWhenPaused { get; set; } = false;
    [JsonProperty] public bool UpdateMotionProviderWithoutScript { get; set; } = false;
    [JsonProperty] public DeviceAxis UpdateMotionProviderWithAxis { get; set; } = null;
    [JsonProperty] public string SelectedMotionProvider { get; set; } = null;
    [JsonProperty] public bool SpeedLimitEnabled { get; set; } = false;
    [JsonProperty] public double MaximumSecondsPerStroke { get; set; } = 0.1;

    public AxisSettings(DeviceAxis axis)
    {
        SmartLimitTargetValue = axis.DefaultValue;
        AutoHomeTargetValue = axis.DefaultValue;

        if (axis.Name == "R0" || axis.Name == "R1" || axis.Name == "R2")
        {
            if (DeviceAxis.TryParse("L0", out var strokeAxis))
                UpdateMotionProviderWithAxis = strokeAxis;

            var providerName = typeof(RandomMotionProvider).GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            if (providerName != null)
                SelectedMotionProvider = providerName;

            UpdateMotionProviderWithoutScript = true;
        }
    }
}

internal enum SmartLimitMode
{
    Value,
    Speed
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class SyncSettings : PropertyChangedBase
{
    [JsonProperty] public double Duration { get; set; } = 4;
    [JsonProperty] public bool SyncOnMediaResourceChanged { get; set; } = true;
    [JsonProperty] public bool SyncOnMediaPlayPause { get; set; } = true;
    [JsonProperty] public bool SyncOnSeek { get; set; } = true;
    [JsonProperty] public bool SyncOnAutoHomeStartEnd { get; set; } = true;
}

internal sealed class MediaLoopSegment : PropertyChangedBase
{
    private double? _startPosition;
    private double? _endPosition;

    public double? StartPosition
    {
        get => _startPosition;
        set
        {
            if (value >= _endPosition)
                EndPosition = null;

            _startPosition = value switch
            {
                double position when !double.IsFinite(position) => null,
                _ => value,
            };
        }
    }

    public double? EndPosition
    {
        get => _endPosition;
        set
        {
            if (value <= _startPosition)
                StartPosition = null;

            _endPosition = value switch
            {
                double position when !double.IsFinite(position) => null,
                _ => value,
            };
        }
    }

    public bool IsValid => StartPosition != null && EndPosition != null;

    public bool TryGetPositions(out double startPosition, out double endPosition)
    {
        startPosition = endPosition = double.NaN;
        if (!IsValid)
            return false;

        startPosition = _startPosition.Value;
        endPosition = _endPosition.Value;
        return true;
    }

    public void Clear()
    {
        StartPosition = null;
        EndPosition = null;
    }
}

public sealed class SeekRequestEventArgs(TimeSpan position) : EventArgs
{
    public TimeSpan Position { get; } = position;
}