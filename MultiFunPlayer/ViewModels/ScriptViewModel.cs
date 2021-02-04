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

namespace MultiFunPlayer.ViewModels
{
    public class ScriptViewModel : Screen, IDeviceAxisValueProvider, IDisposable,
        IHandle<VideoPositionMessage>, IHandle<VideoPlayingMessage>, IHandle<VideoFileChangedMessage>, IHandle<VideoDurationMessage>, IHandle<VideoSpeedMessage>, IHandle<AppSettingsMessage>
    {
        private readonly float _syncDuration = 4;

        private Thread _updateThread;
        private CancellationTokenSource _cancellationSource;
        private float _syncTime;

        public bool IsPlaying { get; set; }
        public float CurrentPosition { get; set; }
        public float PlaybackSpeed { get; set; }
        public float VideoDuration { get; set; }
        public float GlobalOffset { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, AxisState> AxisStates { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, ScriptAxisSettings> AxisSettings { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>> ScriptKeyframes { get; }
        public BindableCollection<ScriptLibrary> ScriptLibraries { get; }

        public VideoFile VideoFile { get; set; }

        public bool IsSyncing => _syncTime < _syncDuration;
        public float SyncProgress => !IsSyncing ? 100 : (MathF.Pow(2, 10 * (_syncTime / _syncDuration - 1)) * 100);

        public ScriptViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            AxisStates = new ObservableConcurrentDictionary<DeviceAxis, AxisState>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisState()));
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, ScriptAxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new ScriptAxisSettings()));
            ScriptLibraries = new BindableCollection<ScriptLibrary>();

            VideoFile = null;

            VideoDuration = float.NaN;
            CurrentPosition = float.NaN;
            PlaybackSpeed = 1;

            IsPlaying = false;

            ScriptKeyframes = new ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>>();
            _cancellationSource = new CancellationTokenSource();

            _updateThread = new Thread(UpdateThread) { IsBackground = true };
            _updateThread.Start(_cancellationSource.Token);

            ResetSync(false);
        }

        private void UpdateThread(object parameter)
        {
            var token = (CancellationToken)parameter;
            var stopwatch = new Stopwatch();
            var uiUpdateInterval = 1f / 60f;
            var uiUpdateTime = 0f;

            stopwatch.Start();

            var randomizer = new OpenSimplex(0);
            while (!token.IsCancellationRequested)
            {
                if (!IsPlaying)
                {
                    Thread.Sleep(10);
                    stopwatch.Restart();
                    continue;
                }

                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                {
                    var state = AxisStates[axis];
                    lock (state)
                    {
                        if (state.Valid)
                        {
                            if (!ScriptKeyframes.TryGetValue(axis, out var keyframes))
                                continue;

                            var axisPosition = GetAxisPosition(axis);
                            while (state.NextIndex < keyframes.Count - 1 && keyframes[state.NextIndex].Position < axisPosition)
                                state.PrevIndex = state.NextIndex++;

                            if (!keyframes.ValidateIndex(state.PrevIndex) || !keyframes.ValidateIndex(state.NextIndex))
                                continue;

                            var prev = keyframes[state.PrevIndex];
                            var next = keyframes[state.NextIndex];
                            var settings = AxisSettings[axis];
                            var newValue = MathUtils.Map(axisPosition, prev.Position, next.Position,
                                settings.Inverted ? 1 - prev.Value : prev.Value,
                                settings.Inverted ? 1 - next.Value : next.Value);

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
                }

                Thread.Sleep(2);

                uiUpdateTime += (float)stopwatch.Elapsed.TotalSeconds;
                if (uiUpdateTime >= uiUpdateInterval)
                {
                    uiUpdateTime = 0;
                    Execute.OnUIThread(() =>
                    {
                        foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                            AxisStates[axis].Notify();
                    });
                }

                CurrentPosition += (float)stopwatch.Elapsed.TotalSeconds * PlaybackSpeed;
                if (IsSyncing && AxisStates.Values.Any(x => x.Valid))
                {
                    _syncTime += (float)stopwatch.Elapsed.TotalSeconds;
                    NotifyOfPropertyChange(nameof(IsSyncing));
                    NotifyOfPropertyChange(nameof(SyncProgress));
                }

                stopwatch.Restart();
            }
        }

        #region Events
        public void Handle(VideoFileChangedMessage message)
        {
            if (VideoFile == null && message.VideoFile == null)
                return;
            if (VideoFile != null && message.VideoFile != null)
                if (string.Equals(VideoFile.Name, message.VideoFile.Name, StringComparison.OrdinalIgnoreCase))
                   return;

            VideoFile = message.VideoFile;
            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                AxisSettings[axis].Script = null;

            ResetSync(isSyncing: VideoFile != null);
            TryMatchFiles(overwrite: true, null);

            if (VideoFile == null)
            {
                VideoDuration = float.NaN;
                CurrentPosition = float.NaN;
                PlaybackSpeed = 1;
            }

            UpdateFiles(AxisFilesChangeType.Update, null);
        }

        public void Handle(VideoPlayingMessage message)
        {
            if (!IsPlaying && message.IsPlaying)
                ResetSync();

            IsPlaying = message.IsPlaying;
        }

        public void Handle(VideoDurationMessage message)
        {
            VideoDuration = (float)(message.Duration?.TotalSeconds ?? float.NaN);
        }

        public void Handle(VideoSpeedMessage message)
        {
            PlaybackSpeed = message.Speed;
        }

        public void Handle(VideoPositionMessage message)
        {
            var newPosition = (float)(message.Position?.TotalSeconds ?? float.NaN);

            var error = float.IsFinite(CurrentPosition) ? newPosition - CurrentPosition : 0;
            var wasSeek = MathF.Abs(error) > 1.0f;
            CurrentPosition = newPosition;
            if (error < 1.0f)
                CurrentPosition -= MathUtils.Map(MathF.Abs(error), 1, 0, 0, 0.75f) * error;

            if (!float.IsFinite(CurrentPosition))
                return;

            if (wasSeek)
                ResetSync();

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
                var settings = new JObject
                {
                    { nameof(AxisSettings), JObject.FromObject(AxisSettings) },
                    { nameof(ScriptLibraries), JArray.FromObject(ScriptLibraries) }
                };

                message.Settings["Script"] = settings;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.ContainsKey("Script"))
                    return;

                var settings = message.Settings["Script"] as JObject;
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
            }
        }
        #endregion

        #region Common
        private void SearchForValidIndices(DeviceAxis axis, AxisState state)
        {
            if (!ScriptKeyframes.TryGetValue(axis, out var keyframes))
                return;

            lock (state)
            {
                var bestIndex = keyframes.BinarySearch(new Keyframe(GetAxisPosition(axis)), new KeyframePositionComparer());
                if (bestIndex >= 0)
                {
                    state.PrevIndex = bestIndex;
                    state.NextIndex = bestIndex + 1;
                }
                else
                {
                    bestIndex = ~bestIndex;
                    if (bestIndex == keyframes.Count)
                    {
                        state.PrevIndex = keyframes.Count;
                        state.NextIndex = keyframes.Count;
                    }
                    else
                    {
                        state.PrevIndex = bestIndex - 1;
                        state.NextIndex = bestIndex;
                    }
                }
            }
        }

        private void UpdateFiles(AxisFilesChangeType changeType, params DeviceAxis[] changedAxes)
        {
            void Clear(DeviceAxis axis)
            {
                ScriptKeyframes.Remove(axis);
                AxisSettings[axis].Script = null;

                var state = AxisStates[axis];
                lock (state)
                    state.Invalidate();
            }

            bool Load(DeviceAxis axis, IScriptFile file)
            {
                var result = true;
                try
                {
                    var document = JObject.Parse(file.Data);
                    if (!document.TryGetValue("rawActions", out var actions) || (actions as JArray)?.Count == 0)
                        if (!document.TryGetValue("actions", out actions) || (actions as JArray)?.Count == 0)
                            return false;

                    var keyframes = new List<Keyframe>();
                    foreach (var child in actions)
                    {
                        var position = child["at"].ToObject<long>() / 1000.0f;
                        if (position < 0)
                            continue;

                        var value = child["pos"].ToObject<float>() / 100;
                        keyframes.Add(new Keyframe(position, value));
                    }

                    ScriptKeyframes.AddOrUpdate(axis, keyframes);
                }
                catch
                {
                    ScriptKeyframes.Remove(axis);
                    result = false;
                }

                var state = AxisStates[axis];
                lock (state)
                    state.Invalidate();

                return result;
            }

            void Update(DeviceAxis axis)
            {
                var file = AxisSettings[axis].Script;
                if (file == null)
                    Clear(axis);
                else
                    Load(axis, file);
            }

            changedAxes ??= EnumUtils.GetValues<DeviceAxis>();
            if (changeType == AxisFilesChangeType.Clear)
            {
                foreach (var axis in changedAxes)
                    Clear(axis);
            }
            else if(changeType == AxisFilesChangeType.Update)
            {
                foreach (var axis in changedAxes)
                    Update(axis);
            }

            foreach (var (axis, settings) in AxisSettings.Where(x => Array.Exists(changedAxes, a => a == x.Value.LinkAxis)))
            {
                settings.Script = AxisSettings[settings.LinkAxis.Value].Script;
                settings.RandomizerSeed = MathUtils.Random(short.MinValue, short.MaxValue);
                Update(axis);
            }

            NotifyOfPropertyChange(nameof(ScriptKeyframes));
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
                        if (AxisSettings[DeviceAxis.L0].Script == null || overwrite)
                        {
                            AxisSettings[DeviceAxis.L0].Script = generator();
                            updated.Add(DeviceAxis.L0);
                        }
                        return true;
                    }
                }

                foreach (var axis in axes)
                {
                    if (funscriptWithoutExtension.EndsWith(axis.Name(), StringComparison.OrdinalIgnoreCase)
                     || funscriptWithoutExtension.EndsWith(axis.AltName(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (AxisSettings[axis].Script == null || overwrite)
                        {
                            AxisSettings[axis].Script = generator();
                            updated.Add(axis);
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
                foreach (var zipFile in library.EnumerateFiles($"{videoWithoutExtension}.zip"))
                    TryMatchArchive(zipFile.FullName);

                foreach (var funscriptFile in library.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                    TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));
            }

            if (Directory.Exists(VideoFile.Source))
            {
                var sourceDirectory = new DirectoryInfo(VideoFile.Source);
                TryMatchArchive(Path.Join(sourceDirectory.FullName, $"{videoWithoutExtension}.zip"));

                foreach (var funscriptFile in sourceDirectory.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                    TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));
            }

            return updated.Distinct();
        }

        private float GetAxisPosition(DeviceAxis axis) => CurrentPosition - GlobalOffset - AxisSettings[axis].Offset;
        public float GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

        private void ResetSync(bool isSyncing = true)
        {
            Interlocked.Exchange(ref _syncTime, isSyncing ? 0 : _syncDuration);
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
        public void OnOpenVideoLocation()
        {
            if (VideoFile == null)
                return;

            if(Directory.Exists(VideoFile.Source))
                Process.Start("explorer.exe", VideoFile.Source);
        }
        #endregion

        #region AxisSettings
        public void OnAxisDrop(object sender, DragEventArgs e)
        {
            if (!(sender is FrameworkElement element && element.DataContext is KeyValuePair<DeviceAxis, ScriptAxisSettings> pair))
                return;

            var drop = e.Data.GetData(DataFormats.FileDrop);
            if (drop is string[] paths)
            {
                var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
                if (path == null)
                    return;

                pair.Value.Script = ScriptFile.FromPath(path);
                UpdateFiles(AxisFilesChangeType.Update, pair.Key);
            }
        }

        public void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Link;
        }

        public void OnAxisOpenFolder(DeviceAxis axis)
        {
            if (AxisSettings[axis].Script == null)
                return;

            Process.Start("explorer.exe", AxisSettings[axis].Script.Source.DirectoryName);
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

            AxisSettings[axis].Script = ScriptFile.FromFileInfo(new FileInfo(dialog.FileName));
            UpdateFiles(AxisFilesChangeType.Update, axis);
        }

        public void OnAxisClear(DeviceAxis axis) => UpdateFiles(AxisFilesChangeType.Clear, axis);
        public void OnAxisReload(DeviceAxis axis)
        {
            var updated = TryMatchFiles(overwrite: true, axis);
            if (updated.Any())
            {
                UpdateFiles(AxisFilesChangeType.Update, axis);
                ResetSync();
            }
        }

        public void OnSliderDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
                slider.Value = 0;
        }

        [SuppressPropertyChangedWarnings]
        public void OnRandomizerSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!(sender is FrameworkElement element && element.DataContext is KeyValuePair<DeviceAxis, ScriptAxisSettings> pair))
                return;

            if (pair.Value.LinkAxis == null)
                return;

            ResetSync();
        }

        [SuppressPropertyChangedWarnings]
        public void OnSelectedLinkAxisChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is FrameworkElement element && element.DataContext is KeyValuePair<DeviceAxis, ScriptAxisSettings> pair))
                return;

            var axis = pair.Key;
            var settings = pair.Value;

            if (e.AddedItems.TryGet<DeviceAxis>(0, out var added) && added == axis)
            {
                settings.LinkAxis = e.RemovedItems.TryGet<DeviceAxis>(0, out var removed) ? removed : null;
            }
            else if(settings.LinkAxis == null)
            {
                UpdateFiles(AxisFilesChangeType.Clear, axis);
            }
            else if(settings.LinkAxis != null)
            {
                settings.Script = AxisSettings[settings.LinkAxis.Value].Script;
                UpdateFiles(AxisFilesChangeType.Update, axis);
            }

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

            var updated = TryMatchFiles(overwrite: false, null);
            if (updated.Any())
            {
                UpdateFiles(AxisFilesChangeType.Update, updated.ToArray());
                ResetSync();
            }
        }

        public void OnLibraryDelete(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
                return;

            ScriptLibraries.Remove(library);

            var updated = TryMatchFiles(overwrite: false, null);
            if (updated.Any())
            {
                UpdateFiles(AxisFilesChangeType.Update, updated.ToArray());
                ResetSync();
            }
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

    [DoNotNotify]
    public class AxisState : INotifyPropertyChanged
    {
        public int PrevIndex { get; set; } = -1;
        public int NextIndex { get; set; } = -1;
        public float Value { get; set; } = float.NaN;

        public bool Valid => PrevIndex >= 0 && NextIndex >= 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void Invalidate() => PrevIndex = NextIndex = -1;

        public void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Valid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class ScriptAxisSettings : PropertyChangedBase
    {
        [JsonIgnore] public IScriptFile Script { get; set; } = null;
        [JsonProperty] public DeviceAxis? LinkAxis { get; set; } = null;
        [JsonProperty] public int RandomizerSeed { get; set; } = 0;
        [JsonProperty] public int RandomizerStrength { get; set; } = 0;
        [JsonProperty] public int RandomizerSpeed { get; set; } = 0;
        [JsonProperty] public bool Inverted { get; set; } = false;
        [JsonProperty] public float Offset { get; set; } = 0;
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

        internal IEnumerable<FileInfo> EnumerateFiles(string searchPattern)
            => Directory.EnumerateFiles(searchPattern, Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }

    [DebuggerDisplay("[{Position}, {Value}]")]
    public class Keyframe
    {
        public float Position { get; set; }
        public float Value { get; set; }

        public Keyframe(float position) : this(position, float.NaN) { }
        public Keyframe(float position, float value)
        {
            Position = position;
            Value = value;
        }

        public void Deconstruct(out float position, out float value)
        {
            position = Position;
            value = Value;
        }
    }

    public class KeyframePositionComparer : IComparer<Keyframe>
    {
        public int Compare(Keyframe x, Keyframe y)
            => Comparer<float>.Default.Compare(x.Position, y.Position);
    }
}
