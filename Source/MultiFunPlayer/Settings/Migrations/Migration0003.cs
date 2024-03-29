using Newtonsoft.Json.Linq;
using NLog;

namespace MultiFunPlayer.Settings.Migrations;

internal sealed class Migration0003 : AbstractConfigMigration
{
    protected override Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Migrate(JObject settings)
    {
        if (TryGetValue<JObject>(settings, "OutputTarget", out var outputTargets))
        {
            var nameToTypeMap = new Dictionary<string, string>()
            {
                ["Buttplug.io"] = "MultiFunPlayer.OutputTarget.ViewModels.ButtplugOutputTargetViewModel, MultiFunPlayer",
                ["Network"] = "MultiFunPlayer.OutputTarget.ViewModels.NetworkOutputTargetViewModel, MultiFunPlayer",
                ["Pipe"] = "MultiFunPlayer.OutputTarget.ViewModels.PipeOutputTargetViewModel, MultiFunPlayer",
                ["Serial"] = "MultiFunPlayer.OutputTarget.ViewModels.SerialOutputTargetViewModel, MultiFunPlayer"
            };

            var items = new JArray();
            foreach (var property in GetProperties(outputTargets, nameToTypeMap.Keys))
            {
                var outputTarget = property.Value as JObject;
                AddPropertiesByName(outputTarget, new Dictionary<string, JToken>()
                {
                    ["$type"] = nameToTypeMap[property.Name],
                    ["$index"] = 0
                });

                items.Add(outputTarget);
                RemoveProperty(property);
            }

            AddPropertyByName(outputTargets, "Items", items);
            EditPropertyByName(outputTargets, "ActiveItem", v => $"{v}/0");
        }

        base.Migrate(settings);
    }
}
