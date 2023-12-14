using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Script;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.IO;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Looping Script")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class LoopingScriptMotionProvider(DeviceAxis target, IEventAggregator eventAggregator) : AbstractMotionProvider(target, eventAggregator)
{
    private double _time;

    private double _scriptStart;
    private double _scriptEnd;
    private int _scriptIndex;

    public IScriptResource Script { get; private set; }

    [JsonProperty] public FileInfo SourceFile { get; set; } = null;
    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;

    public void OnSourceFileChanged()
    {
        var result = FunscriptReader.Default.FromFileInfo(SourceFile);
        Script = result.IsMultiAxis
            ? DeviceAxis.TryParse("L0", out var strokeAxis) && result.Resources.TryGetValue(strokeAxis, out var resource)
                ? resource
                : result.Resources.Values.FirstOrDefault()
            : result.Resource;

        _scriptIndex = -1;
        _scriptStart = Script?.Keyframes[0].Position ?? double.NaN;
        _scriptEnd = Script?.Keyframes[^1].Position ?? double.NaN;
        _time = _scriptStart;
    }

    public override void Update(double deltaTime)
    {
        if (Script == null)
            return;

        var keyframes = Script.Keyframes;
        if (keyframes == null || keyframes.Count == 0)
            return;

        if (_time >= _scriptEnd || _scriptIndex + 1 >= keyframes.Count)
        {
            _scriptIndex = -1;
            _time = _scriptStart;
        }

        _scriptIndex = keyframes.AdvanceIndex(_scriptIndex, _time);
        if (!keyframes.ValidateIndex(_scriptIndex) || !keyframes.ValidateIndex(_scriptIndex + 1))
            return;

        var newValue = MathUtils.Clamp01(keyframes.Interpolate(_scriptIndex, _time, InterpolationType));
        Value = MathUtils.Map(newValue, 0, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }

    public void SelectScript()
    {
        var dialog = new OpenFileDialog()
        {
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "Funscript files (*.funscript)|*.funscript"
        };

        if (dialog.ShowDialog() != true)
            return;

        SourceFile = new FileInfo(dialog.FileName);
    }

    public static void RegisterActions(IShortcutManager s, Func<DeviceAxis, LoopingScriptMotionProvider> getInstance)
    {
        void UpdateProperty(DeviceAxis axis, Action<LoopingScriptMotionProvider> callback)
        {
            var motionProvider = getInstance(axis);
            if (motionProvider != null)
                callback(motionProvider);
        }

        AbstractMotionProvider.RegisterActions(s, getInstance);
        var name = typeof(LoopingScriptMotionProvider).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

        #region LoopingMotionProvider::Script
        s.RegisterAction<DeviceAxis, string>($"MotionProvider::{name}::Script::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Script path"),
            (axis, script) => UpdateProperty(axis, p => p.SourceFile = new FileInfo(script)));
        #endregion

        #region LoopingMotionProvider::Interpolation
        s.RegisterAction<DeviceAxis, InterpolationType>($"MotionProvider::{name}::Interpolation::Set",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Interpolation type").WithItemsSource(Enum.GetValues<InterpolationType>()),
            (axis, interpolation) => UpdateProperty(axis, p => p.InterpolationType = interpolation));
        #endregion
    }
}
