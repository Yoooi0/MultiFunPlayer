using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;

namespace MultiFunPlayer.MotionProvider.ViewModels;

[DisplayName("Looping Script")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class LoopingScriptMotionProviderViewModel : AbstractMotionProvider
{
    private float _time;
    private long _lastTime;

    private float _scriptStart;
    private float _scriptEnd;
    private int _scriptIndex;

    public IScriptFile Script { get; private set; }

    [JsonProperty] public float Speed { get; set; } = 1;
    [JsonProperty] public float Minimum { get; set; } = 0;
    [JsonProperty] public float Maximum { get; set; } = 100;
    [JsonProperty] public FileInfo SourceFile { get; set; } = null;
    [JsonProperty] public InterpolationType InterpolationType { get; set; } = InterpolationType.Pchip;

    public void OnSourceFileChanged()
    {
        Script = ScriptFile.FromFileInfo(SourceFile, true);
        _scriptIndex = 0;
        _scriptStart = Script?.Keyframes?.First().Position ?? float.NaN;
        _scriptEnd = Script?.Keyframes?.Last().Position ?? float.NaN;
        _time = 0;
    }

    public LoopingScriptMotionProviderViewModel()
    {
        _lastTime = Environment.TickCount64;
        _time = 0;
    }

    public override void Update()
    {
        if (Script == null)
            return;

        var keyframes = Script.Keyframes;
        var currentTime = Environment.TickCount64;
        if (keyframes == null || keyframes.Count == 0)
            return;

        if (_time >= _scriptEnd || _scriptIndex >= keyframes.Count)
        {
            _scriptIndex = 0;
            _time = _scriptStart;
        }

        while (_scriptIndex + 1 < keyframes.Count && keyframes[_scriptIndex + 1].Position < _time)
            _scriptIndex++;

        if (!keyframes.ValidateIndex(_scriptIndex) || !keyframes.ValidateIndex(_scriptIndex + 1))
            return;

        var newValue = default(float);
        if (keyframes.IsRawCollection || _scriptIndex == 0 || _scriptIndex + 2 == keyframes.Count || InterpolationType == InterpolationType.Linear)
        {
            var p0 = keyframes[_scriptIndex];
            var p1 = keyframes[_scriptIndex + 1];

            newValue = MathUtils.Interpolate(p0.Position, p0.Value, p1.Position, p1.Value, _time, InterpolationType.Linear);
        }
        else
        {
            var p0 = keyframes[_scriptIndex - 1];
            var p1 = keyframes[_scriptIndex + 0];
            var p2 = keyframes[_scriptIndex + 1];
            var p3 = keyframes[_scriptIndex + 2];

            newValue = MathUtils.Interpolate(p0.Position, p0.Value, p1.Position, p1.Value, p2.Position, p2.Value, p3.Position, p3.Value,
                                             _time, InterpolationType);
        }

        Value = MathUtils.Map(newValue, 0, 1, Minimum / 100, Maximum / 100);

        _time += Speed * (currentTime - _lastTime) / 1000.0f;
        _lastTime = currentTime;
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
