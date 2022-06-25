using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using Newtonsoft.Json;
using Stylet;
using System.ComponentModel;
using System.IO;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Looping Script")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class LoopingScriptMotionProviderViewModel : AbstractMotionProvider
{
    private double _time;

    private double _scriptStart;
    private double _scriptEnd;
    private int _scriptIndex;

    public IScriptResource Script { get; private set; }

    [JsonProperty] public FileInfo SourceFile { get; set; } = null;
    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;

    public LoopingScriptMotionProviderViewModel(DeviceAxis target, IEventAggregator eventAggregator)
        : base(target, eventAggregator) { }

    public void OnSourceFileChanged()
    {
        Script = ScriptResource.FromFileInfo(SourceFile, true);
        _scriptIndex = 0;
        _scriptStart = Script?.Keyframes?.First().Position ?? double.NaN;
        _scriptEnd = Script?.Keyframes?.Last().Position ?? double.NaN;
        _time = 0;
    }

    public override void Update(double deltaTime)
    {
        if (Script == null)
            return;

        var keyframes = Script.Keyframes;
        if (keyframes == null || keyframes.Count == 0)
            return;

        if (_time >= _scriptEnd || _scriptIndex >= keyframes.Count)
        {
            _scriptIndex = 0;
            _time = _scriptStart;
        }

        _scriptIndex = keyframes.AdvanceIndex(_scriptIndex, _time);
        if (!keyframes.ValidateIndex(_scriptIndex) || !keyframes.ValidateIndex(_scriptIndex + 1))
            return;

        var newValue = keyframes.Interpolate(_scriptIndex, _time, InterpolationType);
        Value = MathUtils.Map(newValue, 0, 1, Minimum / 100, Maximum / 100);
        _time += Speed * deltaTime;
    }

    public void SelectScript()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            EnsureFileExists = true
        };
        dialog.Filters.Add(new CommonFileDialogFilter("Funscript", "*.funscript"));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        SourceFile = new FileInfo(dialog.FileName);
    }
}
