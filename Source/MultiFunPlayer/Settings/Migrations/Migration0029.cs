using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0029 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        var updateContextPropertyMap = new Dictionary<string, string[]>()
        {
            ["ThreadPolledUpdateContext"] = [],
            ["AsyncPolledUpdateContext"] = [],
            ["ThreadFixedUpdateContext"] = ["UpdateInterval", "UsePreciseSleep"],
            ["AsyncFixedUpdateContext"] = ["UpdateInterval"],
            ["TCodeThreadFixedUpdateContext"] = ["UpdateInterval", "UsePreciseSleep", "OffloadElapsedTime", "SendDirtyValuesOnly"],
            ["TCodeAsyncFixedUpdateContext"] = ["UpdateInterval", "OffloadElapsedTime", "SendDirtyValuesOnly"],
        };

        var outputTargetUpdateContextMap = new Dictionary<string, string[]>()
        {
            ["MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTarget, MultiFunPlayer"] = ["AsyncFixedUpdateContext", "AsyncPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.FileOutputTarget, MultiFunPlayer"] = ["ThreadFixedUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTarget, MultiFunPlayer"] = ["TCodeThreadFixedUpdateContext", "ThreadPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTarget, MultiFunPlayer"] = ["TCodeThreadFixedUpdateContext", "ThreadPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.TcpOutputTarget, MultiFunPlayer"] = ["TCodeThreadFixedUpdateContext", "ThreadPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.TheHandyOutputTarget, MultiFunPlayer"] = ["AsyncPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.UdpOutputTarget, MultiFunPlayer"] = ["TCodeThreadFixedUpdateContext", "ThreadPolledUpdateContext"],
            ["MultiFunPlayer.OutputTarget.ViewModels.WebSocketOutputTarget, MultiFunPlayer"] = ["TCodeAsyncFixedUpdateContext", "AsyncPolledUpdateContext"],
        };

        foreach(var outputTarget in SelectObjects(settings, "$.OutputTarget.Items[*]"))
        {
            if (!TryGetValue<JValue>(outputTarget, "$type", out var type))
                continue;

            foreach (var updateContextName in outputTargetUpdateContextMap[type.ToString()])
            {
                var contextSettings = CreateChildObjects(outputTarget, "UpdateContextSettings", updateContextName);
                foreach(var property in updateContextPropertyMap[updateContextName])
                    MovePropertyByName(outputTarget, property, contextSettings);
            }
        }

        base.Migrate(settings);
    }
}
