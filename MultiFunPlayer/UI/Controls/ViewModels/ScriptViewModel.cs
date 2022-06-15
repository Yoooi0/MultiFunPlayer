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
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly IMediaResourceFactory _mediaResourceFactory;
    private Thread _updateThread;
    private CancellationTokenSource _cancellationSource;

    public bool IsPlaying { get; set; }
    public float CurrentPosition { get; set; }
    public float PlaybackSpeed { get; set; }
    public float MediaDuration { get; set; }

    public ObservableConcurrentDictionary<DeviceAxis, AxisModel> AxisModels { get; set; }
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisState> AxisStates { get; }
    public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, KeyframeCollection> AxisKeyframes { get; }

    public Dictionary<string, Type> MediaPathModifierTypes { get; }
    public IMotionProviderManager MotionProviderManager { get; }

    public MediaResourceInfo MediaResource { get; set; }

    [JsonProperty] public float GlobalOffset { get; set; }
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
    [JsonProperty] public float AutoSkipToScriptStartOffset { get; set; } = 5;

    public bool IsSyncing => AxisStates.Values.Any(s => s.SyncTime > 0);
    public float SyncProgress => !IsSyncing ? 100 : GetSyncProgress(AxisStates.Values.Max(s => s.SyncTime), SyncSettings.Duration) * 100;

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

        MediaResource = null;

        MediaDuration = float.NaN;
        CurrentPosition = float.NaN;
        PlaybackSpeed = 1;

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

        public float LastValue => _state.Value;
        public float Value { get; set; } = float.NaN;
        public bool IsDirty { get; set; } = false;
        public bool InsideGap { get; set; } = false;

        public AxisUpdateContext(AxisState state)
        {
            _state = state;
        }

        public void Commit()
        {
            if (!float.IsFinite(LastValue) && float.IsFinite(Value))
                IsDirty = true;

            _state.InsideGap = InsideGap;
            _state.IsDirty = IsDirty;
            if (IsDirty)
                _state.Value = Value;
        }
    }

    private void UpdateThread(CancellationToken token)
    {
        const float uiUpdateInterval = 1f / 60f;
        var uiUpdateTime = 0f;
        var deltaTime = 0f;

        while (!token.IsCancellationRequested)
        {
            var updateStartTicks = Stopwatch.GetTimestamp();

            var dirty = UpdateValues();
            UpdateUi();

            Thread.Sleep(IsPlaying || dirty ? 2 : 10);
            deltaTime = (Stopwatch.GetTimestamp() - updateStartTicks) / (float)Stopwatch.Frequency;
        }

        bool UpdateValues()
        {
            if (IsPlaying)
            {
                MediaPositionSync.FixedUpdate(CurrentPosition, deltaTime * PlaybackSpeed);
                CurrentPosition += deltaTime * PlaybackSpeed * MediaPositionSync.PlaybackSpeed;
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

                    if(!context.IsDirty && float.IsFinite(state.OverrideValue))
                    {
                        context.IsDirty = MathF.Abs(context.LastValue - state.OverrideValue) > 0.000001f;
                        context.Value = state.OverrideValue;
                        state.OverrideValue = float.NaN;
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
                static bool SpeedLimitInternal(AxisSettings settings, float deltaTime, ref AxisUpdateContext context)
                {
                    if (!settings.SpeedLimitEnabled)
                        return false;

                    var step = context.Value - context.LastValue;
                    if (!float.IsFinite(step))
                        return false;
                    if (MathF.Abs(step) < 0.000001f)
                        return false;

                    var speed = step / deltaTime;
                    var maxSpeed = 1 / settings.MaximumSecondsPerStroke;
                    if (MathF.Abs(speed / maxSpeed) < 1)
                        return false;
                    if (!float.IsFinite(maxSpeed))
                        return false;

                    context.Value = MathUtils.Clamp01(context.LastValue + maxSpeed * deltaTime * MathF.Sign(speed));
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
                if (!float.IsFinite(context.Value))
                    return NoUpdate();
                if (settings.SmartLimitPoints == null || settings.SmartLimitPoints.Count == 0)
                    return NoUpdate();

                var x = AxisStates[settings.SmartLimitInputAxis].Value * 100;
                var factor = Interpolation.Linear(settings.SmartLimitPoints, p => (float)p.X, p => (float)p.Y, x) / 100;
                state.IsSmartLimited = factor < 1;

                context.Value = MathUtils.Clamp01(MathUtils.Lerp(axis.DefaultValue, context.Value, factor));
                return MathF.Abs(context.LastValue - context.Value) > 0.000001f;
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
                    state.Index = keyframes.BinarySearch(GetAxisPosition(axis, axisPosition));
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

                context.Value = MathUtils.Clamp01(axis.DefaultValue + (scriptValue - axis.DefaultValue) * settings.Scale / 100.0f);
                return MathF.Abs(context.LastValue - context.Value) > 0.000001f;
            }

            bool UpdateMotionProvider(DeviceAxis axis, AxisState state, AxisSettings settings, ref AxisUpdateContext context)
            {
                if (settings.SelectedMotionProvider == null)
                    return false;

                var providerValue = float.NaN;
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
                    if (!float.IsFinite(providerValue))
                        return false;

                    var blendT = IsPlaying && state.InsideScript ? MathUtils.Clamp01(settings.MotionProviderBlend / 100) : 1;
                    var blendFrom = float.IsFinite(context.Value) ? context.Value : axis.DefaultValue;
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

                if (!float.IsFinite(providerValue))
                    return false;

                context.Value = providerValue;
                return MathF.Abs(context.LastValue - context.Value) > 0.000001f;
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

                    if (!float.IsFinite(state.Value))
                        return false;

                    if (!settings.AutoHomeEnabled)
                        return false;

                    if (settings.AutoHomeDuration < 0.0001f)
                    {
                        context.Value = axis.DefaultValue;
                        return context.Value != context.LastValue;
                    }

                    state.AutoHomeTime += deltaTime;
                    var t = (state.AutoHomeTime - settings.AutoHomeDelay) / settings.AutoHomeDuration;
                    if (t < 0 || t > 1)
                        return false;

                    context.Value = MathUtils.Clamp01(MathUtils.Lerp(state.Value, axis.DefaultValue, MathF.Pow(2, 10 * (t - 1))));
                    return MathF.Abs(context.LastValue - context.Value) > 0.000001f;
                }

                return state.IsAutoHoming = UpdateAutoHomeInternal(ref context);
            }

            bool UpdateSync(DeviceAxis axis, AxisState state, ref AxisUpdateContext context)
            {
                if (state.SyncTime <= 0)
                    return false;

                var t = GetSyncProgress(state.SyncTime, SyncSettings.Duration);
                state.SyncTime -= deltaTime;

                if (!float.IsFinite(context.Value))
                    return false;

                var from = !float.IsFinite(context.LastValue) ? axis.DefaultValue : context.LastValue;
                context.Value = MathUtils.Clamp01(MathUtils.Lerp(from, context.Value, t));

                return MathF.Abs(context.LastValue - context.Value) > 0.000001f;
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
        {
            MediaDuration = float.NaN;
            CurrentPosition = float.NaN;
            PlaybackSpeed = 1;
        }

        InvalidateState(null);
    }

    public void Handle(MediaPlayingChangedMessage message)
    {
        if (IsPlaying == message.IsPlaying)
            return;

        Logger.Info("Received {0} [IsPlaying: {1}]", nameof(MediaPlayingChangedMessage), message.IsPlaying);

        if (SyncSettings.SyncOnMediaPlayPause)
            ResetSync();

        if (!IsPlaying && message.IsPlaying)
            MediaPositionSync.Reset();

        IsPlaying = message.IsPlaying;
    }

    public void Handle(MediaDurationChangedMessage message)
    {
        var newDuration = (float)(message.Duration?.TotalSeconds ?? float.NaN);
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

        MediaPositionSync.Reset();
    }

    public void Handle(MediaPositionChangedMessage message)
    {
        void UpdateCurrentPosition(float newPosition)
        {
            foreach (var axis in DeviceAxis.All)
                Monitor.Enter(AxisStates[axis]);

            MediaPositionSync.Reset();
            CurrentPosition = newPosition;

            foreach (var axis in DeviceAxis.All)
                Monitor.Exit(AxisStates[axis]);
        }

        var newPosition = (float)(message.Position?.TotalSeconds ?? float.NaN);
        Logger.Trace("Received {0} [Position: {1}]", nameof(MediaPositionChangedMessage), message.Position?.ToString());

        if (!float.IsFinite(newPosition))
        {
            CurrentPosition = float.NaN;
            return;
        }

        if (!float.IsFinite(CurrentPosition))
        {
            ResetSync();
            UpdateCurrentPosition(newPosition);
            return;
        }

        var error = float.IsFinite(CurrentPosition) ? newPosition - CurrentPosition : 0;
        var wasSeek = MathF.Abs(error) > 1.0f;
        if (wasSeek)
        {
            Logger.Debug("Detected seek: {0}", error);
            if (SyncSettings.SyncOnSeek)
                ResetSync();

            UpdateCurrentPosition(newPosition);
        }
        else
        {
            MediaPositionSync.Update(CurrentPosition, newPosition, PlaybackSpeed);
        }
    }

    private static class MediaPositionSync
    {
        private static readonly object _lockObject = new object();

        private static float _desiredSpeed;
        private static float _desiredCorrection;
        private static float _currentCorrection;
        private static float _interpolatedDesired;

        private static State _state;
        private static List<(float Timestamp, float Position)> _list;
        private static bool _listFilled;
        private static int _lastItemIndex;
        private static int _statePreference;

        public static float PlaybackSpeed => CorrectedDesiredSpeed * _currentCorrection;
        public static float CorrectedDesiredSpeed => _desiredSpeed + _statePreference * 0.0005f;

        static MediaPositionSync() => Reset();

        public static void Reset()
        {
            _list ??= new List<(float Timestamp, float Position)>();

            _list.Clear();
            _listFilled = false;
            _lastItemIndex = 0;

            _desiredSpeed = 1;
            _desiredCorrection = 1;
            _currentCorrection = 1;

            _state = State.Idle;
            _statePreference = 0;
        }

        public static void FixedUpdate(float currentPosition, float deltaTime)
        {
            lock (_lockObject)
            {
                _currentCorrection = MathUtils.Lerp(_currentCorrection, _desiredCorrection, 0.008f);

                var interpolatedCurrent = currentPosition + deltaTime * CorrectedDesiredSpeed * _currentCorrection;
                _interpolatedDesired += deltaTime * _desiredSpeed;

                var error = interpolatedCurrent - _interpolatedDesired;
                UpdateState(error);
            }
        }

        public static void Update(float currentPosition, float desiredPosition, float playbackSpeed)
        {
            lock (_lockObject)
            {
                PushPosition(desiredPosition);
                UpdateSpeed(playbackSpeed);

                var error = currentPosition - desiredPosition;
                UpdateState(error);
            }
        }

        private static void PushPosition(float position)
        {
            if (_list.Count != 0)
            {
                var timeSinceLast = Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency - _list[_lastItemIndex].Timestamp;
                if (timeSinceLast < 0.5f)
                    return;
            }

            if (_listFilled)
            {
                _lastItemIndex = (_lastItemIndex + 1) % _list.Count;
                _list[_lastItemIndex] = (Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency, position);
            }
            else
            {
                _lastItemIndex = _list.Count;
                _list.Add((Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency, position));
                _listFilled = (_list[^1].Timestamp - _list[0].Timestamp) > 10f;
            }

            _interpolatedDesired = position;
        }

        private static void UpdateSpeed(float playbackSpeed)
        {
            _desiredSpeed =
                _list.Count >= 2 ?
                    !_listFilled
                    ? EnumerateSpeeds(0, _lastItemIndex, playbackSpeed)
                        .DefaultIfEmpty(1).Average()
                    : EnumerateSpeeds((_lastItemIndex + 1) % _list.Count, _list.Count - 1, playbackSpeed)
                        .Concat(EnumerateSpeeds(0, _lastItemIndex, playbackSpeed))
                        .DefaultIfEmpty(1).Average()
                : 1;

            static IEnumerable<float> EnumerateSpeeds(int from, int to, float playbackSpeed)
            {
                for (int i = from, j = from + 1; j <= to; i = j++)
                {
                    var (ti, pi) = _list[i];
                    var (tj, pj) = _list[j];
                    var speed = (pj - pi) / (playbackSpeed * (tj - ti));
                    if (!float.IsFinite(speed))
                        continue;

                    yield return speed;
                }
            }
        }

        private static void UpdateState(float error)
            => UpdateState((error, _state) switch
            {
                var (e, s) when e > 0 && s == State.Behind => State.Idle,
                var (e, s) when e < 0 && s == State.Ahead => State.Idle,
                var (e, s) when e > 0 && e > 0.020f && s == State.Idle => State.Ahead,
                var (e, s) when e < 0 && e < -0.020f && s == State.Idle => State.Behind,
                var (e, s) when MathF.Abs(e) < 0.005f && s != State.Idle => State.Idle,
                var (_, s) => s
            });

        private static void UpdateState(State newState)
        {
            if (newState == State.Behind && _state == State.Idle)
                _statePreference++;
            else if (newState == State.Ahead && _state == State.Idle)
                _statePreference--;

            _statePreference = MathUtils.Clamp(_statePreference, -30, 30);

            _state = newState;
            _desiredCorrection = _state switch
            {
                State.Idle => 1f,
                State.Ahead => 0.95f,
                State.Behind => 1.05f,
                _ => throw new NotSupportedException(),
            };
        }

        private enum State
        {
            Idle,
            Ahead,
            Behind
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

            if (settings.TryGetValue<float>(nameof(GlobalOffset), out var globalOffset)) GlobalOffset = globalOffset;
            if (settings.TryGetValue<bool>(nameof(ValuesContentVisible), out var valuesContentVisible)) ValuesContentVisible = valuesContentVisible;
            if (settings.TryGetValue<bool>(nameof(MediaContentVisible), out var mediaContentVisible)) MediaContentVisible = mediaContentVisible;
            if (settings.TryGetValue<bool>(nameof(AxisContentVisible), out var axisContentVisible)) AxisContentVisible = axisContentVisible;
            if (settings.TryGetValue<int>(nameof(HeatmapBucketCount), out var heatmapBucketCount)) HeatmapBucketCount = heatmapBucketCount;
            if (settings.TryGetValue<bool>(nameof(HeatmapInvertY), out var heatmapInvertY)) HeatmapInvertY = heatmapInvertY;
            if (settings.TryGetValue<bool>(nameof(HeatmapShowStrokeLength), out var heatmapShowStrokeLength)) HeatmapShowStrokeLength = heatmapShowStrokeLength;
            if (settings.TryGetValue<bool>(nameof(AutoSkipToScriptStartEnabled), out var autoSkipToScriptStartEnabled)) AutoSkipToScriptStartEnabled = autoSkipToScriptStartEnabled;
            if (settings.TryGetValue<float>(nameof(AutoSkipToScriptStartOffset), out var autoSkipToScriptStartOffset)) AutoSkipToScriptStartOffset = autoSkipToScriptStartOffset;

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

    private void InvalidateState(params DeviceAxis[] axes) => InvalidateState(axes?.AsEnumerable());
    private void InvalidateState(IEnumerable<DeviceAxis> axes = null)
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

    private float GetAxisPosition(DeviceAxis axis) => GetAxisPosition(axis, CurrentPosition);
    private float GetAxisPosition(DeviceAxis axis, float position) => position - GlobalOffset - AxisSettings[axis].Offset;
    public float GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetSyncProgress(float time, float duration) => MathUtils.Clamp01(MathF.Pow(2, -10 * MathUtils.Clamp01(time / duration)));

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

        if(script != null)
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

    private void SkipGap(float minimumSkip = 0, params DeviceAxis[] axes) => SkipGap(axes?.AsEnumerable(), minimumSkip);
    private void SkipGap(IEnumerable<DeviceAxis> axes = null, float minimumSkip = 0)
    {
        float? GetSkipPosition(DeviceAxis axis)
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

        var maybeSkipPosition = AxisKeyframes.Keys.Select(a => GetSkipPosition(a)).MinBy(x => x ?? float.PositiveInfinity);
        var currentPosition = CurrentPosition;
        if (maybeSkipPosition is not float skipPosition || currentPosition >= skipPosition || (skipPosition - currentPosition) <= minimumSkip)
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

        SeekMediaToPercent((float)e.GetPosition(element).X / (float)element.ActualWidth);
    }

    private void SeekMediaToPercent(float percent)
    {
        if (!float.IsFinite(MediaDuration) || !float.IsFinite(percent))
            return;

        _eventAggregator.Publish(new MediaSeekMessage(TimeSpan.FromSeconds(MediaDuration * MathUtils.Clamp01(percent))));
    }

    private void SeekMediaToTime(float time)
    {
        if (!float.IsFinite(MediaDuration) || !float.IsFinite(time))
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

        _autoSkipToScriptStartTask = Task.Delay(1000, _cancellationSource.Token)
                                         .ContinueWith(_ => SeekMediaToScriptStart(AutoSkipToScriptStartOffset, onlyWhenBefore: true));
    }

    private void SeekMediaToScriptStart(float offset, bool onlyWhenBefore)
    {
        if (!float.IsFinite(MediaDuration) || !float.IsFinite(offset))
            return;

        var startPosition = AxisKeyframes.Select(x => x.Value)
                                         .NotNull()
                                         .Select(ks => ks.TryGet(ks.SkipGap(index: 0), out var k) ? k.Position : default(float?))
                                         .FirstOrDefault();
        if (startPosition == null)
            return;

        var targetMediaTime = MathF.Max(MathF.Min(startPosition.Value, MediaDuration) - offset, 0);
        if (onlyWhenBefore && targetMediaTime <= CurrentPosition)
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
                if(axis != null)
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

    public void SetAxisValue(DeviceAxis axis, float value, bool offset = false)
    {
        if (axis == null)
            return;

        var state = AxisStates[axis];
        lock (state)
        {
            if (offset)
            {
                if (!float.IsFinite(state.Value))
                    state.OverrideValue = axis.DefaultValue;

                state.OverrideValue = MathUtils.Clamp01(state.Value + value);
            }
            else
            {
                state.OverrideValue = value;
            }
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
            while(current != null)
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

        var(axis, model) = pair;
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

        if(MediaResource != null)
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
                                                                .WithCallback((_, offset) => SeekMediaToTime(CurrentPosition + offset)));
        s.RegisterAction("Media::Position::Time::Set", b => b.WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                                                             .WithCallback((_, value) => SeekMediaToTime(value)));

        s.RegisterAction("Media::Position::Percent::Offset", b => b.WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                                                                   .WithCallback((_, offset) => SeekMediaToPercent(CurrentPosition / MediaDuration + offset / 100)));
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
                  .WithSetting<float>(p => p.WithLabel("Value offset"))
                  .WithCallback((_, axis, offset) => SetAxisValue(axis, offset, offset: true)));

        s.RegisterAction("Axis::Value::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value"))
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
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = MathUtils.Clamp(s.MaximumSecondsPerStroke + offset, 0.001f, 2f))));

        s.RegisterAction("Axis::SpeedLimitSecondsPerStroke::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0:F3}s/stroke"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.MaximumSecondsPerStroke = MathUtils.Clamp(value, 0.001f, 2f))));
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
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.AutoHomeDelay = MathF.Max(0, s.AutoHomeDelay + offset))));

        s.RegisterAction("Axis::AutoHomeDelay::Set",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, value) => UpdateSettings(axis, s => s.AutoHomeDelay = MathF.Max(0, value))));
        #endregion

        #region Axis::AutoHomeDuration
        s.RegisterAction("Axis::AutoHomeDuration::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}s"))
                  .WithCallback((_, axis, offset) => UpdateSettings(axis, s => s.AutoHomeDuration = MathF.Max(0, s.AutoHomeDuration + offset))));

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
    [DoNotNotify] public float Value { get; set; } = float.NaN;
    [DoNotNotify] public float OverrideValue { get; set; } = float.NaN;

    [DoNotNotify] public bool Invalid => Index == int.MinValue;
    [DoNotNotify] public bool BeforeScript => Index == -1;
    [DoNotNotify] public bool AfterScript => Index == int.MaxValue;
    [DoNotNotify] public bool InsideScript => Index >= 0 && Index != int.MaxValue;

    [DoNotNotify] public bool InsideGap { get; set; } = false;

    [DoNotNotify] public float SyncTime { get; set; } = 0;
    [DoNotNotify] public float AutoHomeTime { get; set; } = 0;

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

    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;
    [JsonProperty] public bool AutoHomeEnabled { get; set; } = false;
    [JsonProperty] public float AutoHomeDelay { get; set; } = 5;
    [JsonProperty] public float AutoHomeDuration { get; set; } = 3;
    [JsonProperty] public bool Inverted { get; set; } = false;
    [JsonProperty] public float Offset { get; set; } = 0;
    [JsonProperty] public float Scale { get; set; } = 100;
    [JsonProperty] public bool Bypass { get; set; } = false;
    [JsonProperty] public float MotionProviderBlend { get; set; } = 100;
    [JsonProperty] public bool MotionProviderFillGaps { get; set; } = false;
    [JsonProperty] public float MotionProviderMinimumGapDuration { get; set; } = 5;
    [JsonProperty] public bool UpdateMotionProviderWhenPaused { get; set; } = false;
    [JsonProperty] public bool UpdateMotionProviderWithoutScript { get; set; } = true;
    [JsonProperty] public DeviceAxis UpdateMotionProviderWithAxis { get; set; } = null;
    [JsonProperty] public string SelectedMotionProvider { get; set; } = null;
    [JsonProperty] public bool SpeedLimitEnabled { get; set; } = false;
    [JsonProperty] public float MaximumSecondsPerStroke { get; set; } = 0.1f;
}

[JsonObject(MemberSerialization.OptIn)]
public class SyncSettings : PropertyChangedBase
{
    [JsonProperty] public float Duration { get; set; } = 4;
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
