using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0021 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        var _replaceMap = new Dictionary<string, string>()
        {
            ["MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels.FindReplaceMediaPathModifierViewModel, MultiFunPlayer"] = "MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels.FindReplaceMediaPathModifier, MultiFunPlayer",
            ["MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels.UriToLocalMediaPathModifierViewModel, MultiFunPlayer"] = "MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels.UriToLocalMediaPathModifier, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.TheHandyOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.TheHandyOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.UdpOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.UdpOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.FileOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.FileOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.TcpOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.TcpOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.OutputTarget.ViewModels.WebSocketOutputTargetViewModel, MultiFunPlayer"] = "MultiFunPlayer.OutputTarget.ViewModels.WebSocketOutputTarget, MultiFunPlayer",
            ["MultiFunPlayer.MotionProvider.ViewModels.CustomCurveMotionProviderViewModel, MultiFunPlayer"] = "MultiFunPlayer.MotionProvider.ViewModels.CustomCurveMotionProvider, MultiFunPlayer",
            ["MultiFunPlayer.MotionProvider.ViewModels.LoopingScriptMotionProviderViewModel, MultiFunPlayer"] = "MultiFunPlayer.MotionProvider.ViewModels.LoopingScriptMotionProvider, MultiFunPlayer",
            ["MultiFunPlayer.MotionProvider.ViewModels.PatternMotionProviderViewModel, MultiFunPlayer"] = "MultiFunPlayer.MotionProvider.ViewModels.PatternMotionProvider, MultiFunPlayer",
            ["MultiFunPlayer.MotionProvider.ViewModels.RandomMotionProviderViewModel, MultiFunPlayer"] = "MultiFunPlayer.MotionProvider.ViewModels.RandomMotionProvider, MultiFunPlayer"
        };

        EditPropertiesByPath(settings, "$..$type",
            v => _replaceMap.ContainsKey(v.ToString()),
            v => _replaceMap[v.ToString()]);

        base.Migrate(settings);
    }
}