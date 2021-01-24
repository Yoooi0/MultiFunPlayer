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
using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;

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
        public bool IsSyncing { get; set; }
        public float CurrentPosition { get; set; }
        public float PlaybackSpeed { get; set; }
        public float VideoDuration { get; set; }
        public float GlobalOffset { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, AxisState> AxisStates { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, ScriptAxisSettings> AxisSettings { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>> ScriptKeyframes { get; }
        public BindableCollection<DirectoryInfo> ScriptDirectories { get; }

        public FileInfo VideoFile { get; set; }

        public float SyncProgress => !IsSyncing ? 100 : (MathF.Pow(2, 10 * (_syncTime / _syncDuration - 1)) * 100);

        public ScriptViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            AxisStates = new ObservableConcurrentDictionary<DeviceAxis, AxisState>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisState()));
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, ScriptAxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new ScriptAxisSettings()));
            ScriptDirectories = new BindableCollection<DirectoryInfo>();

            VideoFile = null;

            VideoDuration = float.NaN;
            CurrentPosition = float.NaN;
            PlaybackSpeed = 1;

            IsPlaying = false;
            IsSyncing = false;

            _syncTime = 0;
            ScriptKeyframes = new ObservableConcurrentDictionary<DeviceAxis, List<Keyframe>>();
            _cancellationSource = new CancellationTokenSource();

            _updateThread = new Thread(UpdateThread) { IsBackground = true };
            _updateThread.Start(_cancellationSource.Token);
        }

        private void UpdateThread(object parameter)
        {
            var token = (CancellationToken)parameter;
            var stopwatch = new Stopwatch();

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
                            Execute.OnUIThread(() => state.Value = newValue);
                        }
                        else
                        {
                            var newValue = axis.DefaultValue();
                            if (IsSyncing)
                                newValue = MathUtils.Lerp(!float.IsFinite(state.Value) ? axis.DefaultValue() : state.Value, newValue, SyncProgress / 100);
                            Execute.OnUIThread(() => state.Value = newValue);
                        }
                    }
                }

                //stopwatch.PreciseSleep(3, token);

                Thread.Sleep(2);
                CurrentPosition += (float)stopwatch.Elapsed.TotalSeconds * PlaybackSpeed;
                if (IsSyncing && AxisStates.Values.Any(x => x.Valid))
                {
                    _syncTime += (float)stopwatch.Elapsed.TotalSeconds;
                    IsSyncing = _syncTime < _syncDuration;
                    NotifyOfPropertyChange(nameof(SyncProgress));
                }

                stopwatch.Restart();
            }
        }

        public void Handle(VideoFileChangedMessage message)
        {
            if (VideoFile == null && message.VideoFile == null)
                return;
            if (VideoFile != null && message.VideoFile != null)
                if (string.Equals(VideoFile.FullName, message.VideoFile.FullName, StringComparison.OrdinalIgnoreCase))
                   return;

            VideoFile = message.VideoFile;
            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                AxisSettings[axis].Script = null;

            ResetSync(isSyncing: VideoFile != null);
            TryMatchFiles(overwrite: true);

            if (VideoFile == null)
            {
                VideoDuration = float.NaN;
                CurrentPosition = float.NaN;
                PlaybackSpeed = 1;
            }

            UpdateFiles(AxisFilesChangeType.Update, null);
        }

        private IEnumerable<DeviceAxis> TryMatchFiles(bool overwrite)
        {
            if (VideoFile == null)
                return Enumerable.Empty<DeviceAxis>();

            var updated = new List<DeviceAxis>();
            bool TryMatchFile(string fileName, Func<IScriptFile> generator)
            {
                var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
                var funscriptWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (string.Equals(funscriptWithoutExtension, videoWithoutExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (AxisSettings[DeviceAxis.L0].Script == null || overwrite)
                    {
                        AxisSettings[DeviceAxis.L0].Script = generator();
                        updated.Add(DeviceAxis.L0);
                    }
                    return true;
                }

                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
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

            var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
            foreach (var funscriptFile in ScriptDirectories.SelectMany(x => x.EnumerateFiles($"{videoWithoutExtension}*.funscript")))
                TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));

            var zipPath = Path.Join(VideoFile.DirectoryName, $"{videoWithoutExtension}.zip");
            if (File.Exists(zipPath))
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries.Where(e => string.Equals(Path.GetExtension(e.FullName), ".funscript", StringComparison.OrdinalIgnoreCase)))
                    TryMatchFile(entry.Name, () => ScriptFile.FromZipArchiveEntry(zipPath, entry));
            }

            foreach (var funscriptFile in VideoFile.Directory.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));

            return updated.Distinct();
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
                    { nameof(ScriptDirectories), JArray.FromObject(ScriptDirectories.Select(x => x.FullName)) }
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
                    foreach (var (axis, axisSettings) in axisSettingsToken.ToObject<Dictionary<DeviceAxis, ScriptAxisSettings>>())
                    {
                        //TODO: persistent tab content breaks when replacing objects
                        // AxisSettings[axis] = axisSettings;

                        AxisSettings[axis].LinkAxis = axisSettings.LinkAxis;
                        AxisSettings[axis].RandomizerSeed = axisSettings.RandomizerSeed;
                        AxisSettings[axis].RandomizerStrength = axisSettings.RandomizerStrength;
                        AxisSettings[axis].RandomizerSpeed = axisSettings.RandomizerSpeed;
                        AxisSettings[axis].Inverted = axisSettings.Inverted;
                        AxisSettings[axis].Offset = axisSettings.Offset;
                    }
                }

                if(settings.TryGetValue(nameof(ScriptDirectories), out var scriptDirectoriesToken))
                {
                    foreach (var directory in scriptDirectoriesToken.ToObject<List<string>>())
                    {
                        if (!Directory.Exists(directory) || ScriptDirectories.Any(x => string.Equals(x.FullName, directory)))
                            continue;

                        ScriptDirectories.Add(new DirectoryInfo(directory));
                    }
                }
            }
        }

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

            foreach (var (axis, settings) in AxisSettings.Where(x => x.Value.LinkAxis != null))
            {
                settings.Script = AxisSettings[settings.LinkAxis.Value].Script;
                settings.RandomizerSeed = MathUtils.Random(short.MinValue, short.MaxValue);
                Update(axis);
            }

            NotifyOfPropertyChange(nameof(ScriptKeyframes));
        }

        private float GetAxisPosition(DeviceAxis axis) => CurrentPosition - GlobalOffset - AxisSettings[axis].Offset;
        public float GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

        public void OnOpenVideoLocation()
        {
            if (VideoFile == null)
                return;

            Process.Start("explorer.exe", VideoFile.DirectoryName);
        }

        public async void OnOpenScriptDirectories()
        {
            _ = await DialogHost.Show(new ScriptDirectoriesDialog(ScriptDirectories)).ConfigureAwait(true);

            var updated = TryMatchFiles(overwrite: false);
            UpdateFiles(AxisFilesChangeType.Update, updated.ToArray());
        }

        public void OnDrop(object sender, DragEventArgs e)
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

        public void OnAxisOpen(DeviceAxis axis)
        {
            var dialog = new CommonOpenFileDialog()
            {
                InitialDirectory = Path.GetDirectoryName(VideoFile?.FullName) ?? string.Empty,
                EnsureFileExists = true
            };
            dialog.Filters.Add(new CommonFileDialogFilter("Funscript files", "*.funscript"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            AxisSettings[axis].Script = ScriptFile.FromFileInfo(new FileInfo(dialog.FileName));
            UpdateFiles(AxisFilesChangeType.Update, axis);
        }

        public void OnAxisClear(DeviceAxis axis) => UpdateFiles(AxisFilesChangeType.Clear, axis);
        public void OnAxisReload(DeviceAxis axis) => UpdateFiles(AxisFilesChangeType.Update, axis);

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

        [SuppressPropertyChangedWarnings]
        public void OnOffsetSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ResetSync();

            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                SearchForValidIndices(axis, AxisStates[axis]);
        }

        private void ResetSync(bool isSyncing = true)
        {
            IsSyncing = isSyncing;
            Interlocked.Exchange(ref _syncTime, 0);
            NotifyOfPropertyChange(nameof(SyncProgress));
        }

        public void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Link;
        }

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

    public class AxisState : PropertyChangedBase
    {
        public int PrevIndex { get; set; } = -1;
        public int NextIndex { get; set; } = -1;
        public float Value { get; set; } = float.NaN;

        public bool Valid => PrevIndex >= 0 && NextIndex >= 0;
        public void Invalidate()
        {
            PrevIndex = NextIndex = -1;
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
