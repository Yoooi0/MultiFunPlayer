using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0021 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, string> _replaceMap = new()
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

    public override void Migrate(JObject settings)
    {
        MigrateTypeValues(settings);

        base.Migrate(settings);
    }

    private void MigrateTypeValues(JObject settings)
    {
        Logger.Info("Migrating \"$type\" values");
        foreach (var value in settings.SelectTokens("$..['$type']").OfType<JValue>())
        {
            var oldValue = value.ToString();
            if (!_replaceMap.ContainsKey(oldValue))
                continue;

            var newValue = _replaceMap[oldValue];
            value.Value = newValue;
            Logger.Info("Changed \"$type\" value from \"{0}\" to \"{1}\"", oldValue, newValue);
        }
    }
}