using MultiFunPlayer.Common;
using Stylet;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.IO.Compression;
using PropertyChanged;
using MaterialDesignExtensions.Controls;
using System.Threading.Tasks;

namespace MultiFunPlayer.ViewModels
{
    public class ValuesViewModel : PropertyChangedBase, IDeviceAxisValueProvider, IHandle<VideoPositionMessage>, IHandle<VideoPlayingMessage>, IHandle<VideoFileChangedMessage>, IDisposable
    {
        private readonly float _syncDuration = 5;

        private readonly ConcurrentDictionary<DeviceAxis, List<Keyframe>> _scripKeyframes;
        private readonly Thread _updateThread;
        private readonly CancellationTokenSource _cancellationSource;
        private float _syncTime;

        public bool IsPlaying { get; set; }
        public bool IsSyncing { get; set; }
        public float CurrentPosition { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, AxisState> AxisStates { get; set; }
        public ObservableConcurrentDictionary<DeviceAxis, AxisSettings> AxisSettings { get; set; }
        public AxisSettings SelectedAxisSettings { get; set; }
        public FileInfo VideoFile { get; set; }

        public ValuesViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            AxisStates = new ObservableConcurrentDictionary<DeviceAxis, AxisState>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisState()));
            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, AxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new AxisSettings()));
            SelectedAxisSettings = AxisSettings[DeviceAxis.L0];

            VideoFile = null;
            CurrentPosition = float.NaN;
            IsPlaying = false;
            IsSyncing = false;

            _syncTime = 0;
            _scripKeyframes = new ConcurrentDictionary<DeviceAxis, List<Keyframe>>();
            _cancellationSource = new CancellationTokenSource();

            _updateThread = new Thread(UpdateThread) { IsBackground = true };
            _updateThread.Start(_cancellationSource.Token);
        }

        private void UpdateThread(object parameter)
        {
            var token = (CancellationToken)parameter;
            var stopwatch = new Stopwatch();

            stopwatch.Start();
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
                    if (!_scripKeyframes.TryGetValue(axis, out var keyframes))
                        continue;

                    if (!AxisStates.TryGetValue(axis, out var state))
                        continue;

                    lock (state)
                    {
                        if (!state.Valid)
                            continue;

                        var settings = AxisSettings[axis];
                        while (state.NextIndex < keyframes.Count - 1 && keyframes[state.NextIndex].Position < CurrentPosition + settings.Offset)
                            state.PrevIndex = state.NextIndex++;

                        if (!keyframes.ValidateIndex(state.PrevIndex) || !keyframes.ValidateIndex(state.NextIndex))
                            continue;

                        var prev = keyframes[state.PrevIndex];
                        var next = keyframes[state.NextIndex];

                        if ((CurrentPosition <= prev.Position || CurrentPosition >= next.Position)
                         && (Math.Abs(CurrentPosition - prev.Position) > 1.0 || Math.Abs(CurrentPosition - next.Position) > 1.0))
                            continue;

                        var newValue = float.NaN;
                        if (settings.Inverted)
                            newValue = MathUtils.Map(CurrentPosition + settings.Offset, prev.Position, next.Position, 1 - prev.Value, 1 - next.Value);
                        else
                            newValue = MathUtils.Map(CurrentPosition + settings.Offset, prev.Position, next.Position, prev.Value, next.Value);

                        if (IsSyncing)
                            newValue = MathUtils.Lerp(!float.IsFinite(state.Value) ? axis.DefaultValue() : state.Value, newValue, (float)Math.Pow(_syncTime / _syncDuration, 3));

                        Execute.OnUIThread(() => state.Value = newValue);
                    }
                }

                //stopwatch.PreciseSleep(3, token);

                Thread.Sleep(2);
                CurrentPosition += (float)stopwatch.Elapsed.TotalSeconds;
                if (IsSyncing)
                {
                    _syncTime += (float)stopwatch.Elapsed.TotalSeconds;
                    IsSyncing = _syncTime < _syncDuration;
                }

                stopwatch.Restart();
            }
        }

        public void Handle(VideoFileChangedMessage message)
        {
            bool TryMatchFile(string fileName, Func<IScriptFile> generator)
            {
                var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
                var funscriptWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (string.Equals(funscriptWithoutExtension, videoWithoutExtension, StringComparison.OrdinalIgnoreCase))
                {
                    AxisSettings[DeviceAxis.L0].File = generator();
                    return true;
                }

                foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                {
                    if (funscriptWithoutExtension.EndsWith(axis.Name(), StringComparison.OrdinalIgnoreCase)
                     || funscriptWithoutExtension.EndsWith(axis.AltName(), StringComparison.OrdinalIgnoreCase))
                    {
                        AxisSettings[axis].File = generator();
                        return true;
                    }
                }

                return false;
            }

            if (VideoFile == null && message.VideoFile == null)
                return;
            if (VideoFile != null && message.VideoFile != null)
                if (string.Equals(VideoFile.FullName, message.VideoFile.FullName, StringComparison.OrdinalIgnoreCase))
                   return;

            VideoFile = message.VideoFile;
            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
                AxisSettings[axis].File = null;

            IsSyncing = VideoFile != null;
            Interlocked.Exchange(ref _syncTime, 0);

            if (VideoFile != null)
            {
                var videoWithoutExtension = Path.GetFileNameWithoutExtension(VideoFile.Name);
                var zipPath = Path.Join(VideoFile.DirectoryName, $"{videoWithoutExtension}.zip");
                if (File.Exists(zipPath))
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    foreach (var entry in zip.Entries.Where(e => string.Equals(Path.GetExtension(e.FullName), ".funscript", StringComparison.OrdinalIgnoreCase)))
                        TryMatchFile(entry.Name, () => ScriptFile.FromZipArchiveEntry(entry));
                }

                foreach (var funscriptFile in VideoFile.Directory.EnumerateFiles($"{videoWithoutExtension}*.funscript"))
                    TryMatchFile(funscriptFile.Name, () => ScriptFile.FromFileInfo(funscriptFile));
            }

            UpdateFiles(AxisFilesChangeType.Update, null);
        }

        public void Handle(VideoPlayingMessage message)
        {
            if (!IsPlaying && message.IsPlaying)
            {
                IsSyncing = true;
                Interlocked.Exchange(ref _syncTime, 0);
            }

            IsPlaying = message.IsPlaying;
        }

        public void Handle(VideoPositionMessage message)
        {
            var newPosition = (float)message.Position.TotalSeconds;

            var error = float.IsFinite(CurrentPosition) ? newPosition - CurrentPosition : 0;
            var wasSeek = Math.Abs(error) > 1.0;
            CurrentPosition = newPosition;
            if (error < 1.0)
                CurrentPosition -= MathUtils.Map(Math.Abs(error), 1, 0, 0, 0.75f) * error;

            if (!float.IsFinite(CurrentPosition))
                return;

            if (wasSeek)
            {
                IsSyncing = true;
                Interlocked.Exchange(ref _syncTime, 0);
            }

            foreach (var axis in EnumUtils.GetValues<DeviceAxis>())
            {
                if (!_scripKeyframes.TryGetValue(axis, out var keyframes))
                    continue;

                if (!AxisStates.TryGetValue(axis, out var state))
                    continue;

                if (wasSeek || !state.Valid)
                {
                    lock (state)
                    {
                        var settings = AxisSettings[axis];
                        var current = keyframes.BinarySearch(new Keyframe(CurrentPosition + settings.Offset), new KeyframePositionComparer());
                        if (current >= 0)
                        {
                            state.PrevIndex = current;
                            state.NextIndex = current + 1;
                        }
                        else
                        {
                            current = ~current;
                            if (current == keyframes.Count)
                            {
                                state.PrevIndex = keyframes.Count;
                                state.NextIndex = keyframes.Count;
                            }
                            else
                            {
                                state.PrevIndex = current - 1;
                                state.NextIndex = current;
                            }
                        }
                    }
                }
            }
        }

        private void UpdateFiles(AxisFilesChangeType changeType, params DeviceAxis[] changedAxes)
        {
            void Clear(DeviceAxis axis)
            {
                _scripKeyframes.TryRemove(axis, out var _);

                var wasSelected = SelectedAxisSettings == AxisSettings[axis];
                AxisSettings[axis] = new AxisSettings();
                if (wasSelected)
                    SelectedAxisSettings = AxisSettings[axis];

                if (AxisStates.TryGetValue(axis, out var state))
                {
                    lock (state)
                        state.Invalidate();
                }
            }

            bool Load(DeviceAxis axis, IScriptFile file)
            {
                var document = JsonDocument.Parse(file.Data);
                if (!document.RootElement.TryGetProperty("rawActions", out var actions) || actions.GetArrayLength() == 0)
                    if (!document.RootElement.TryGetProperty("actions", out actions) || actions.GetArrayLength() == 0)
                        return false;

                var keyframes = new List<Keyframe>();
                foreach (var child in actions.EnumerateArray())
                {
                    var position = child.GetProperty("at").GetInt64() / 1000.0f;
                    var value = (float)child.GetProperty("pos").GetDouble() / 100;
                    keyframes.Add(new Keyframe(position, value));
                }

                _scripKeyframes.AddOrUpdate(axis, keyframes, (key, oldValue) => keyframes);
                if (AxisStates.TryGetValue(axis, out var state))
                {
                    lock (state)
                        state.Invalidate();
                }

                return true;
            }

            void Update(DeviceAxis axis)
            {
                var file = AxisSettings[axis].File;
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
        }

        public float GetValue(DeviceAxis axis) => MathUtils.Clamp01(AxisStates[axis].Value);

        public void OnDrop(object sender, DragEventArgs e)
        {
            if (!(sender is FrameworkElement element && element.DataContext is DeviceAxis axis))
                return;

            var drop = e.Data.GetData(DataFormats.FileDrop);
            if (drop is string[] paths)
            {
                var path = paths.FirstOrDefault(p => Path.GetExtension(p) == ".funscript");
                AxisSettings[axis].File = ScriptFile.FromPath(path);
                UpdateFiles(AxisFilesChangeType.Update, axis);
            }
        }

        public async Task OnAxisOpen(DeviceAxis axis)
        {
            var dialogArgs = new OpenFileDialogArguments()
            {
                Width = 600,
                Height = 730,
                Filters = "Funscript files|*.funscript",
                CreateNewDirectoryEnabled = true,
                CurrentDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
            };

            var result = await OpenFileDialog.ShowDialogAsync("RootDialog", dialogArgs);
            if (!result.Confirmed || !result.FileInfo.Exists)
                return;

            AxisSettings[axis].File = ScriptFile.FromFileInfo(result.FileInfo);
            UpdateFiles(AxisFilesChangeType.Update, axis);
        }

        public void OnAxisClear(DeviceAxis axis) => UpdateFiles(AxisFilesChangeType.Clear, axis);
        public void OnAxisReload(DeviceAxis axis) => UpdateFiles(AxisFilesChangeType.Update, axis);

        [SuppressPropertyChangedWarnings]
        public void OnAxisSettingsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedAxisSettings == null)
                SelectedAxisSettings = ((KeyValuePair<DeviceAxis, AxisSettings>)e.RemovedItems[0]).Value;
        }

        public void OnAxisSettingsOffsetSliderDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
                slider.Value = 0;
        }

        public void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Link;
        }

        protected virtual void Dispose(bool disposing)
        {
            _cancellationSource.Cancel();
            _updateThread.Join();
            _cancellationSource.Dispose();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
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
            Value = float.NaN;
        }
    }

    public class AxisSettings : PropertyChangedBase
    {
        public IScriptFile File { get; set; } = null;
        public bool Inverted { get; set; } = false;
        public float Offset { get; set; } = 0;
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
