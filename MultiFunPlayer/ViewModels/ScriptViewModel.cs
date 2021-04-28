using MultiFunPlayer.Common;
using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
using MultiFunPlayer.OutputTarget;
using NLog;

namespace MultiFunPlayer.ViewModels
{
    public class ScriptViewModel : Screen, IDeviceAxisValueProvider, IDisposable,
        IHandle<VideoPositionMessage>, IHandle<VideoPlayingMessage>, IHandle<VideoFileChangedMessage>, IHandle<VideoDurationMessage>, IHandle<VideoSpeedMessage>, IHandle<AppSettingsMessage>
    {
        protected Logger Logger = LogManager.GetCurrentClassLogger();

        private Thread _updateThread;
        private CancellationTokenSource _cancellationSource;
        private float _syncTime;

        public bool IsPlaying { get; set; }
        public bool IsValuesPanelExpanded { get; set; }
        public float CurrentPosition { get; set; }
        public float PlaybackSpeed { get; set; }
        public float VideoDuration { get; set; }
        public float GlobalOffset { get; set; }

        public ObservableConcurrentDictionary<DeviceAxis, AxisModel> AxisModels { get; set; }
        public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisState> AxisStates { get; }
        public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisSettings> AxisSettings { get; }
        public ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, KeyframeCollection> AxisKeyframes { get; }

        public BindableCollection<ScriptLibrary> ScriptLibraries { get; }
        public SyncSettings SyncSettings { get; set; }

        public VideoFileInfo VideoFile { get; set; }

        public bool IsSyncing => _syncTime < SyncSettings.Duration;
        public float SyncProgress => !IsSyncing ? 100 : (MathF.Pow(2, 10 * (_syncTime / SyncSettings.Duration - 1)) * 100);

        public ScriptViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            AxisModels = new ObservableConcurrentDictionary<DeviceAxis, AxisModel>(EnumUtils.ToDictionary<DeviceAxis, AxisModel>(_ => new AxisModel()));
            ScriptLibraries = new BindableCollection<ScriptLibrary>();
            SyncSettings = new SyncSettings();

            VideoFile = null;

            VideoDuration = float.NaN;
            CurrentPosition = float.NaN;
            PlaybackSpeed = 1;

            IsPlaying = false;

            AxisStates = new ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisState>(AxisModels, model => model.State);
            AxisSettings = new ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, AxisSettings>(AxisModels, model => model.Settings);
            AxisKeyframes = new ObservableConcurrentDictionaryView<DeviceAxis, AxisModel, KeyframeCollection>(AxisModels, model => model.Script?.Keyframes, "Script");
            _cancellationSource = new CancellationTokenSource();

            _updateThread = new Thread(() => UpdateThread(_cancellationSource.Token)) { IsBackground = true };
            _updateThread.Start();

            ResetSync(false);
        }

        private void UpdateThread(CancellationToken token)
        {
            var stopwatch = new Stopwatch();
            const float uiUpdateInterval = 1f / 60f;
            var uiUpdateTime = 0f;
            var pauseTime = 0f;

            stopwatch.Start();

            var randomizer = new OpenSimplex(0);
            while (!token.IsCancellationRequested)
            {
                var dirty = UpdateValues();
                Thread.Sleep(IsPlaying && dirty ? 2 : 10);

                UpdateUi();

                CurrentPosition += (float)stopwatch.Elapsed.TotalSeconds * PlaybackSpeed;
                pauseTime += (float)stopwatch.Elapsed.TotalSeconds;
                uiUpdateTime += (float)stopwatch.Elapsed.TotalSeconds;

                UpdateSync();

                stopwatch.Restart();
            }

            void UpdateSmartLimit()
            {
                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                {
                    var settings = AxisSettings[axis];
                    if (!settings.SmartLimitEnabled)
                        continue;

                    var limitState = AxisStates[DeviceAxis.L0];
                    if (!limitState.Valid)
                        continue;

                    var state = AxisStates[axis];
                    var value = state.Value;
                    var limitValue = limitState.Value;

                    var factor = MathUtils.Map(limitValue, 0.25f, 0.9f, 1f, 0f);
                    state.Value = MathUtils.Lerp(axis.DefaultValue(), state.Value, factor);
                }
            }

            bool UpdateValues()
            {
                if (IsPlaying)
                    pauseTime = 0;

                var dirty = false;
                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                {
                    if (IsPlaying)
                        dirty |= UpdateValuesPlaying(axis);
                    else
                        dirty |= UpdateValuesPaused(axis);
                }

                UpdateSmartLimit();
                return dirty;

                bool UpdateValuesPlaying(DeviceAxis axis)
                {
                    var state = AxisStates[axis];
                    lock (state)
                    {
                        if (state.Valid)
                        {
                            if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                                return false;

                            var settings = AxisSettings[axis];
                            var axisPosition = GetAxisPosition(axis);

                            while (state.Index + 1 < keyframes.Count - 1 && keyframes[state.Index + 1].Position < axisPosition)
                                state.Index++;

                            var newValue = default(float);
                            var canSmooth = !keyframes.IsRawCollection && keyframes.ValidateIndex(state.Index - 1) && keyframes.ValidateIndex(state.Index + 2);
                            if (settings.SmoothingType == null || !canSmooth)
                            {
                                if (!keyframes.ValidateIndex(state.Index + 1))
                                    return false;

                                var prev = keyframes[state.Index];
                                var next = keyframes[state.Index + 1];

                                newValue = MathUtils.Map(axisPosition, prev.Position, next.Position, prev.Value, next.Value);
                            }
                            else
                            {
                                var p0 = keyframes[state.Index - 1];
                                var p1 = keyframes[state.Index + 0];
                                var p2 = keyframes[state.Index + 1];
                                var p3 = keyframes[state.Index + 2];

                                newValue = MathUtils.Interpolate(p0.Position, p0.Value,
                                                           p1.Position, p1.Value,
                                                           p2.Position, p2.Value,
                                                           p3.Position, p3.Value,
                                                           axisPosition, settings.SmoothingType.Value);
                            }

                            if (settings.Inverted)
                                newValue = 1 - newValue;

                            if (settings.LinkAxis != null)
                            {
                                var speed = MathUtils.Map(settings.RandomizerSpeed, 100, 0, 0.25f, 4);
                                var randomizerValue = (float)(randomizer.Calculate2D(axisPosition / speed, settings.RandomizerSeed) + 1) / 2;
                                newValue = MathUtils.Lerp(newValue, randomizerValue, settings.RandomizerStrength / 100.0f);
                            }

                            if (IsSyncing)
                                newValue = MathUtils.Lerp(!float.IsFinite(state.Value) ? axis.DefaultValue() : state.Value, newValue, SyncProgress / 100);
                            state.Value = newValue;
                        }
                        else
                        {
                            var newValue = axis.DefaultValue();
                            if (IsSyncing)
                                newValue = MathUtils.Lerp(!float.IsFinite(state.Value) ? axis.DefaultValue() : state.Value, newValue, SyncProgress / 100);
                            state.Value = newValue;
                        }
                    }

                    return true;
                }

                bool UpdateValuesPaused(DeviceAxis axis)
                {
                    var state = AxisStates[axis];
                    lock (state)
                    {
                        if (!float.IsFinite(state.Value))
                            return false;

                        var settings = AxisSettings[axis];
                        if (!settings.AutoHomeEnabled)
                            return false;

                        var t = (pauseTime - settings.AutoHomeDelay) / 3;
                        if (t < 0 || t > 1)
                            return false;

                        state.Value = MathUtils.Lerp(state.Value, axis.DefaultValue(), MathF.Pow(2, 10 * (t - 1)));
                        return true;
                    }
                }
            }

            void UpdateUi()
            {
                if (uiUpdateTime >= uiUpdateInterval)
                {
                    uiUpdateTime = 0;
                    if (IsValuesPanelExpanded)
                    {
                        Execute.OnUIThread(() =>
                        {
                            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                                AxisStates[axis].Notify();
                        });
                    }
                }
            }

            void UpdateSync()
            {
                if (IsSyncing && AxisStates.Values.Any(x => x.Valid))
                {
                    _syncTime += (float)stopwatch.Elapsed.TotalSeconds;
                    NotifyOfPropertyChange(nameof(IsSyncing));
                    NotifyOfPropertyChange(nameof(SyncProgress));
                }
            }
        }

        #region Events
        public void Handle(VideoFileChangedMessage message)
        {
            Logger.Info("Received VideoFileChangedMessage [Source: \"{0}\" Name: \"{1}\"]", message.VideoFile?.Source, message.VideoFile?.Name);

            if (VideoFile == null && message.VideoFile == null)
                return;
            if (VideoFile != null && message.VideoFile != null)
                if (string.Equals(VideoFile.Name, message.VideoFile.Name, StringComparison.OrdinalIgnoreCase))
                   return;

            VideoFile = message.VideoFile;
            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                AxisModels[axis].Script = null;

            if(SyncSettings.SyncOnVideoFileChanged)
                ResetSync(isSyncing: VideoFile != null);
            ReloadScript(null);

            if (VideoFile == null)
            {
                VideoDuration = float.NaN;
                CurrentPosition = float.NaN;
                PlaybackSpeed = 1;
            }

            InvalidateState(null);
        }

        public void Handle(VideoPlayingMessage message)
        {
            Logger.Info("Received VideoPlayingMessage [IsPlaying: {0}]", message.IsPlaying);

            if (!IsPlaying && message.IsPlaying)
                if(SyncSettings.SyncOnVideoResume)
                    ResetSync();

            IsPlaying = message.IsPlaying;
        }

        public void Handle(VideoDurationMessage message)
        {
            var newDuration = (float)(message.Duration?.TotalSeconds ?? float.NaN);
            Logger.Info("Received VideoDurationMessage [Duration: {0}]", message.Duration?.ToString());

            VideoDuration = newDuration;
        }

        public void Handle(VideoSpeedMessage message)
        {
            PlaybackSpeed = message.Speed;
        }

        public void Handle(VideoPositionMessage message)
        {
            var newPosition = (float)(message.Position?.TotalSeconds ?? float.NaN);
            Logger.Trace("Received VideoPositionMessage [Position: {0}]", message.Position?.ToString());

            var error = float.IsFinite(CurrentPosition) ? newPosition - CurrentPosition : 0;
            var wasSeek = MathF.Abs(error) > 1.0f;
            CurrentPosition = newPosition;
            if (error < 1.0f)
                CurrentPosition -= MathUtils.Map(MathF.Abs(error), 1, 0, 0, 0.75f) * error;

            if (!float.IsFinite(CurrentPosition))
                return;

            if (wasSeek)
            {
                Logger.Debug("Detected seek: {0}", error);
                if (SyncSettings.SyncOnSeek)
                    ResetSync();
            }

            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
            {
                var state = AxisStates[axis];
                if (wasSeek || !state.Valid)
                    SearchForValidIndices(axis, state);
            }
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                message.Settings["Script"] = new JObject
                {
                    { nameof(AxisSettings), JObject.FromObject(AxisSettings) },
                    { nameof(ScriptLibraries), JArray.FromObject(ScriptLibraries) },
                    { nameof(IsValuesPanelExpanded), JToken.FromObject(IsValuesPanelExpanded) },
                    { nameof(SyncSettings), JObject.FromObject(SyncSettings) }
                };
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "Script"))
                    return;

                if (settings.TryGetValue(nameof(AxisSettings), out var axisSettingsToken))
                {
                    foreach(var property in axisSettingsToken.Children<JProperty>())
                    {
                        if (!Enum.TryParse<DeviceAxis>(property.Name, out var axis))
                            continue;

                        property.Value.Populate(AxisSettings[axis]);
                    }
                }

                if(settings.TryGetValue(nameof(ScriptLibraries), out var scriptDirectoriesToken))
                {
                    foreach (var library in scriptDirectoriesToken.ToObject<List<ScriptLibrary>>())
                    {
                        if (!library.Directory.Exists || ScriptLibraries.Any(x => string.Equals(x.Directory.FullName, library.Directory.FullName)))
                            continue;

                        ScriptLibraries.Add(library);
                    }
                }

                if (settings.TryGetValue(nameof(IsValuesPanelExpanded), out var isValuesPanelExpandedToken))
                    IsValuesPanelExpanded = isValuesPanelExpandedToken.ToObject<bool>();

                if (settings.TryGetValue(nameof(SyncSettings), out var syncSettingsToken))
                    syncSettingsToken.Populate(SyncSettings);
            }
        }
        #endregion

        #region Common
        private void SearchForValidIndices(DeviceAxis axis, AxisState state)
        {
            if (!AxisKeyframes.TryGetValue(axis, out var keyframes) || keyframes == null || keyframes.Count == 0)
                return;

            lock (state)
                state.Index = keyframes.BinarySearch(GetAxisPosition(axis));
        }

        private void MatchAndInvalidate(bool overwrite, params DeviceAxis[] axes)
        {
            var updated = TryMatchFiles(overwrite, axes);
            if (updated.Any())
                InvalidateState(axes);
        }

        private void LinkAndInvalidate(params DeviceAxis[] axes)
        {
            axes ??= EnumUtils.GetValues<DeviceAxis>();
            if (axes.Length == 0)
                return;

            foreach (var axis in axes)
            {
                var model = AxisModels[axis];
                if (model.Settings.LinkAxis == null)
                {
                    if (model.Settings.LinkAxisHasPriority)
                        ResetScript(axis);

                    continue;
                }

                if (model.Script != null)
                {
                    if (model.Settings.LinkAxisHasPriority && model.Script.Origin == ScriptFileOrigin.User)
                        continue;

                    if (!model.Settings.LinkAxisHasPriority && model.Script.Origin != ScriptFileOrigin.Link)
                        continue;
                }

                Logger.Debug("Linked {0} to {1}", axis, model.Settings.LinkAxis.Value);
                model.Script = LinkedScriptFile.LinkTo(AxisModels[model.Settings.LinkAxis.Value].Script);
                model.Settings.RandomizerSeed = MathUtils.Random(short.MinValue, short.MaxValue);
            }

            InvalidateState(axes);
        }

        private void ResetScript(params DeviceAxis[] axes)
        {
            axes ??= EnumUtils.GetValues<DeviceAxis>();
            if (axes.Length == 0)
                return;

            foreach (var axis in axes)
            {
                Logger.Debug("Reset {0} script", axis);
                AxisModels[axis].Script = null;
            }
        }

        private void ReloadScript(params DeviceAxis[] axes)
        {
            axes ??= EnumUtils.GetValues<DeviceAxis>();
            if (axes.Length == 0)
                return;

            foreach (var group in axes.GroupBy(a => AxisModels[a].Settings.LinkAxisHasPriority))
            {
                if (group.Key)
                {
                    LinkAndInvalidate(group.ToArray());
                }
                else
                {
                    MatchAndInvalidate(overwrite: true, group.ToArray());
                    LinkAndInvalidate(group.ToArray());
                }
            }

            ResetSync();
        }

        private void InvalidateState(params DeviceAxis[] changedAxes)
        {
            changedAxes ??= EnumUtils.GetValues<DeviceAxis>();
            if (changedAxes.Length == 0)
                return;

            foreach (var axis in changedAxes)
            {
                var state = AxisStates[axis];
                lock (state)
                    state.Invalidate();
            }
        }

        private IEnumerable<DeviceAxis> TryMatchFiles(bool overwrite, params DeviceAxis[] axes)
        {
            if (VideoFile == null)
                return Enumerable.Empty<DeviceAxis>();

            if (axes == null)
                axes = EnumUtils.GetValues<DeviceAxis>();

            var updated = new List<DeviceAxis>();
            bool TryMatchFile(string fileName, Func<IScriptFile> generator)
            {
                var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
                var funscriptWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (axes.Contains(DeviceAxis.L0))
                {
                    if (string.Equals(funscriptWithoutExtension, videoWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        if (AxisModels[DeviceAxis.L0].Script == null || overwrite)
                        {
                            AxisModels[DeviceAxis.L0].Script = generator();
                            updated.Add(DeviceAxis.L0);

                            Logger.Debug("Matched {0} script to \"{1}\"", DeviceAxis.L0, fileName);
                        }
                        return true;
                    }
                }

                foreach (var axis in axes)
                {
                    if (axis.Names().Any(n => funscriptWithoutExtension.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (AxisModels[axis].Script == null || overwrite)
                        {
                            AxisModels[axis].Script = generator();
                            updated.Add(axis);

                            Logger.Debug("Matched {0} script to \"{1}\"", axis, fileName);
                        }
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
                        TryMatchFile(entry.Name, () => ScriptFile.FromZipArchiveEntry(path, entry));

                    return true;
                }

                return false;
            }

            var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
            foreach (var library in ScriptLibraries)
            {
                Logger.Info("Searching library \"{0}\"", library.Directory);
                foreach (var zipFile in library.EnumerateFiles($"{videoWithoutExtension}.zip"))
                    TryMatchArchive(zipFile.FullName);

                foreach (var funscriptFile in library.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                    TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));
            }

            if (Directory.Exists(VideoFile.Source))
            {
                Logger.Info("Searching video location \"{0}\"", VideoFile.Source);
                var sourceDirectory = new DirectoryInfo(VideoFile.Source);
                TryMatchArchive(Path.Join(sourceDirectory.FullName, $"{videoWithoutExtension}.zip"));

                foreach (var funscriptFile in sourceDirectory.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                    TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));
            }

            foreach (var axis in axes.Except(updated))
            {
                if (overwrite && AxisModels[axis].Script != null)
                {
                    if (AxisModels[axis].Script.Origin != ScriptFileOrigin.User)
                    {
                        ResetScript(axis);
                        updated.Add(axis);
                    }
                }
            }

            return updated.Distinct();
        }

        private float GetAxisPosition(DeviceAxis axis) => CurrentPosition - GlobalOffset - AxisSettings[axis].Offset;
        public float GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

        private void ResetSync(bool isSyncing = true)
        {
            Interlocked.Exchange(ref _syncTime, isSyncing ? 0 : SyncSettings.Duration);
            NotifyOfPropertyChange(nameof(IsSyncing));
            NotifyOfPropertyChange(nameof(SyncProgress));
        }
        #endregion

        #region UI Common
        [SuppressPropertyChangedWarnings]
        public void OnOffsetSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ResetSync();

            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                SearchForValidIndices(axis, AxisStates[axis]);
        }
        #endregion

        #region Video
        public bool CanOpenVideoLocation => VideoFile != null && Directory.Exists(VideoFile.Source);

        public void OnOpenVideoLocation()
        {
            if (VideoFile == null)
                return;

            if (Directory.Exists(VideoFile.Source))
                Process.Start("explorer.exe", VideoFile.Source);
        }
        #endregion

        #region AxisSettings
        public void OnAxisDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
                return;

            var (axis, model) = pair;
            var drop = e.Data.GetData(DataFormats.FileDrop);
            if (drop is string[] paths)
            {
                var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
                if (path == null)
                    return;

                model.Script = ScriptFile.FromPath(path, userLoaded: true);
                InvalidateState(axis);
                ResetSync();
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

            Process.Start("explorer.exe", model.Script.Source.DirectoryName);
        }

        public void OnAxisLoad(DeviceAxis axis)
        {
            var dialog = new CommonOpenFileDialog()
            {
                InitialDirectory = Directory.Exists(VideoFile?.Source) ? VideoFile.Source : string.Empty,
                EnsureFileExists = true
            };
            dialog.Filters.Add(new CommonFileDialogFilter("Funscript files", "*.funscript"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            AxisModels[axis].Script = ScriptFile.FromFileInfo(new FileInfo(dialog.FileName), userLoaded: true);
            InvalidateState(axis);
            ResetSync();
        }

        public void OnAxisClear(DeviceAxis axis)
        {
            ResetScript(axis);
            InvalidateState(axis);
        }

        public void OnAxisReload(DeviceAxis axis)
        {
            ReloadScript(axis);
        }

        private bool MoveScript(DeviceAxis axis, DirectoryInfo directory)
        {
            if (!directory.Exists || AxisModels[axis].Script == null)
                return false;

            try
            {
                var sourceFile = AxisModels[axis].Script.Source;
                File.Move(sourceFile.FullName, Path.Join(directory.FullName, sourceFile.Name));
            }
            catch { return false; }
            return true;
        }

        public void OnAxisMoveToVideo(DeviceAxis axis)
        {
            if (VideoFile != null && MoveScript(axis, new DirectoryInfo(VideoFile.Source)))
                ReloadScript(axis);
        }

        public RelayCommand<DeviceAxis, ScriptLibrary> OnAxisMoveToLibraryCommand => new(OnAxisMoveToLibrary);
        public void OnAxisMoveToLibrary(DeviceAxis axis, ScriptLibrary library)
        {
            if (library?.Directory.Exists == true && MoveScript(axis, library.Directory))
                ReloadScript(axis);
        }

        public void OnSliderDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
                slider.Value = 0;
        }

        [SuppressPropertyChangedWarnings]
        public void OnRandomizerSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
                return;

            var (_, model) = pair;
            if (model.Settings.LinkAxis == null)
                return;

            ResetSync();
        }

        [SuppressPropertyChangedWarnings]
        public void OnLinkAxisPriorityChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
                return;

            var (axis, _) = pair;
            ReloadScript(axis);
        }

        [SuppressPropertyChangedWarnings]
        public void OnInvertedCheckedChanged(object sender, RoutedEventArgs e)
        {
            ResetSync();
        }

        [SuppressPropertyChangedWarnings]
        public void OnSelectedLinkAxisChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<DeviceAxis, AxisModel> pair)
                return;

            var (axis, model) = pair;
            if (e.AddedItems.TryGet<DeviceAxis>(0, out var added) && added == axis)
                model.Settings.LinkAxis = e.RemovedItems.TryGet<DeviceAxis>(0, out var removed) ? removed : null;

            ReloadScript(axis);
        }

        [SuppressPropertyChangedWarnings]
        public void OnSmartLimitCheckedChanged(object sender, RoutedEventArgs e)
        {
            ResetSync();
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
            if (ScriptLibraries.Any(x => string.Equals(x.Directory.FullName, directory.FullName)))
                return;

            ScriptLibraries.Add(new ScriptLibrary(directory));
            ReloadScript(null);
        }

        public void OnLibraryDelete(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
                return;

            ScriptLibraries.Remove(library);
            ReloadScript(null);
        }

        public void OnLibraryOpenFolder(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
                return;

            Process.Start("explorer.exe", library.Directory.FullName);
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

    public enum AxisFilesChangeType
    {
        Clear,
        Update
    }

    public class AxisModel : PropertyChangedBase
    {
        public AxisState State { get; } = new AxisState();
        public AxisSettings Settings { get; } = new AxisSettings();
        public IScriptFile Script { get; set; } = null;
    }

    [DoNotNotify]
    public class AxisState : INotifyPropertyChanged
    {
        public int Index { get; set; } = -1;
        public float Value { get; set; } = float.NaN;

        public bool Valid => Index >= 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void Invalidate() => Index = -1;

        public void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Valid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AxisSettings : PropertyChangedBase
    {
        [JsonProperty] public bool LinkAxisHasPriority { get; set; } = false;
        [JsonProperty] public DeviceAxis? LinkAxis { get; set; } = null;
        [JsonProperty] public bool SmartLimitEnabled { get; set; } = false;
        [JsonProperty] public InterpolationType? SmoothingType { get; set; } = InterpolationType.Pchip;
        [JsonProperty] public bool AutoHomeEnabled { get; set; } = false;
        [JsonProperty] public float AutoHomeDelay { get; set; } = 5;
        [JsonProperty] public int RandomizerSeed { get; set; } = 0;
        [JsonProperty] public int RandomizerStrength { get; set; } = 0;
        [JsonProperty] public int RandomizerSpeed { get; set; } = 0;
        [JsonProperty] public bool Inverted { get; set; } = false;
        [JsonProperty] public float Offset { get; set; } = 0;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class SyncSettings : PropertyChangedBase
    {
        [JsonProperty] public float Duration { get; set; } = 4;
        [JsonProperty] public bool SyncOnVideoFileChanged { get; set; } = true;
        [JsonProperty] public bool SyncOnVideoResume { get; set; } = true;
        [JsonProperty] public bool SyncOnSeek { get; set; } = true;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class ScriptLibrary : PropertyChangedBase
    {
        public ScriptLibrary(DirectoryInfo directory)
        {
            if (directory?.Exists == false)
                throw new DirectoryNotFoundException();

            Directory = directory;
        }

        [JsonProperty] public DirectoryInfo Directory { get; }
        [JsonProperty] public bool Recursive { get; set; }

        public IEnumerable<FileInfo> EnumerateFiles(string searchPattern)
            => Directory.EnumerateFiles(searchPattern, Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }
}
